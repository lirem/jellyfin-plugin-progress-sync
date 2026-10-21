using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.ProgressSync.Models;
using Jellyfin.Plugin.ProgressSync.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProgressSync.Api;

/// <summary>
/// REST API for the Progress Sync dialog and admin page.
/// </summary>
[ApiController]
[Route("ProgressSync")]
[Authorize]
public sealed class ProgressSyncController : ControllerBase
{
    // Jellyfin.Api.Constants.InternalClaimTypes.UserId; that assembly is not available to plugins.
    private const string UserIdClaimType = "Jellyfin-UserId";
    private const string ClientScriptResource = "Jellyfin.Plugin.ProgressSync.Web.progressSync.js";

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ProgressCopier _copier;
    private readonly LinkStore _store;
    private readonly WebInjectionStatus _injectionStatus;
    private readonly ILogger<ProgressSyncController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressSyncController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="copier">The progress copier.</param>
    /// <param name="store">The link store.</param>
    /// <param name="injectionStatus">The web client integration status.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{ProgressSyncController}"/> interface.</param>
    public ProgressSyncController(
        ILibraryManager libraryManager,
        IUserManager userManager,
        ProgressCopier copier,
        LinkStore store,
        WebInjectionStatus injectionStatus,
        ILogger<ProgressSyncController> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _copier = copier;
        _store = store;
        _injectionStatus = injectionStatus;
        _logger = logger;
    }

    /// <summary>
    /// Copies another user's watch progress to the calling user, once.
    /// </summary>
    /// <param name="itemId">A movie, episode, season or series.</param>
    /// <param name="sourceUserId">The user to copy from.</param>
    /// <response code="200">Progress copied.</response>
    /// <response code="400">Unsupported item type or copying from yourself.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Item or source user not found or not accessible.</response>
    /// <returns>The copy result.</returns>
    [HttpPost("Items/{itemId}/CopyFrom/{sourceUserId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CopyResult> CopyFrom([FromRoute] Guid itemId, [FromRoute] Guid sourceUserId)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (sourceUserId.Equals(me.Id))
        {
            return BadRequest("Cannot copy progress from yourself.");
        }

        var source = GetEnabledUser(sourceUserId);
        var item = GetVisibleItem(itemId, me);
        if (source is null || item is null)
        {
            return NotFound();
        }

        if (!IsSupported(item))
        {
            return BadRequest("Unsupported item type.");
        }

        return _copier.CopyScope(source, me, item, UserDataSaveReason.UpdateUserData);
    }

    /// <summary>
    /// Lists the links on an item's series or movie that the calling user can see.
    /// </summary>
    /// <param name="itemId">A movie, episode, season or series.</param>
    /// <response code="200">Links returned.</response>
    /// <response code="400">Item cannot be linked.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Item not found or not accessible.</response>
    /// <returns>The visible links.</returns>
    [HttpGet("Items/{itemId}/Links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<LinkDto>> GetLinks([FromRoute] Guid itemId)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var item = GetVisibleItem(itemId, me);
        if (item is null)
        {
            return NotFound();
        }

        if (LinkRules.ResolveRoot(item) is not { } rootId)
        {
            return BadRequest("Item cannot be linked.");
        }

        return _store.Snapshot.ForRoot(rootId)
            .Where(l => LinkRules.IsVisibleTo(l, me.Id))
            .Select(l => ToDto(l, me.Id))
            .OfType<LinkDto>()
            .OrderBy(l => l.PartnerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Creates (or changes the mode of) a link on an item's series or movie, then copies the partner's
    /// progress to the calling user.
    /// </summary>
    /// <param name="itemId">A movie, episode, season or series.</param>
    /// <param name="request">Partner and mode.</param>
    /// <response code="200">Link stored; see the initial copy fields for the copy outcome.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Item or partner not found or not accessible to both users.</response>
    /// <returns>The link and the initial copy result.</returns>
    [HttpPost("Items/{itemId}/Links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<CreateLinkResponse> CreateLink([FromRoute] Guid itemId, [FromBody] CreateLinkRequest request)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request?.Mode is not { } mode || !Enum.IsDefined(mode) || request.PartnerUserId.Equals(Guid.Empty))
        {
            return BadRequest("PartnerUserId and Mode are required.");
        }

        if (request.PartnerUserId.Equals(me.Id))
        {
            return BadRequest("Cannot link with yourself.");
        }

        var partner = GetEnabledUser(request.PartnerUserId);
        var item = GetVisibleItem(itemId, me);
        if (partner is null || item is null)
        {
            return NotFound();
        }

        if (LinkRules.ResolveRoot(item) is not { } rootId)
        {
            return BadRequest("Item cannot be linked.");
        }

        var rootItem = GetVisibleItem(rootId, me);
        if (rootItem is null || GetVisibleItem(rootId, partner) is null)
        {
            return NotFound();
        }

        var link = _store.Upsert(me.Id, partner.Id, rootId, mode, BuildStaleCheck());
        _logger.LogInformation(
            "Progress Sync: {User} created {Mode} link with {Partner} on {ItemName} ({RootId})",
            me.Username,
            mode,
            partner.Username,
            rootItem.Name,
            rootId);

        CopyResult? copy = null;
        string? copyError = null;
        try
        {
            copy = _copier.CopyScope(partner, me, rootItem, UserDataSaveReason.UpdateUserData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress Sync: initial copy for link {LinkId} failed", link.Id);
            copyError = ex.Message;
        }

        return new CreateLinkResponse(new LinkDto(link.Id, partner.Id, partner.Username, link.Mode, true, true, link.CreatedUtc), copy, copyError);
    }

    /// <summary>
    /// Removes a link. Creators can remove their links; partners can remove two-way links.
    /// </summary>
    /// <param name="linkId">The link id.</param>
    /// <response code="204">Link removed.</response>
    /// <response code="403">The caller is not a user.</response>
    /// <response code="404">Link not found or not removable by the caller.</response>
    /// <returns>No content.</returns>
    [HttpDelete("Links/{linkId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteLink([FromRoute] Guid linkId)
    {
        var me = GetCurrentUser();
        if (me is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var link = _store.Snapshot.Find(linkId);
        if (link is null || !LinkRules.CanRemove(link, me.Id))
        {
            return NotFound();
        }

        _store.Remove(linkId, BuildStaleCheck());
        _logger.LogInformation("Progress Sync: {User} removed {Mode} link {LinkId} on {RootId}", me.Username, link.Mode, link.Id, link.RootItemId);
        return NoContent();
    }

    /// <summary>
    /// Serves the web client script. Anonymous because index.html loads it before sign-in.
    /// </summary>
    /// <param name="v">Script version from the injected URL.</param>
    /// <response code="200">Script returned.</response>
    /// <returns>The script.</returns>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetClientScript([FromQuery] string? v)
    {
        var stream = typeof(ProgressSyncController).Assembly.GetManifestResourceStream(ClientScriptResource);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = string.Equals(v, IndexTransformer.ScriptVersion, StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        return File(stream, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Integration status and all links, for the admin page.
    /// </summary>
    /// <response code="200">Overview returned.</response>
    /// <returns>The overview.</returns>
    [HttpGet("Admin/Overview")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<AdminOverviewDto> GetAdminOverview()
    {
        var links = _store.Snapshot.All
            .Select(l => new AdminLinkDto(
                l.Id,
                _userManager.GetUserById(l.CreatorUserId)?.Username ?? "(deleted)",
                _userManager.GetUserById(l.PartnerUserId)?.Username ?? "(deleted)",
                _libraryManager.GetItemById(l.RootItemId)?.Name ?? "(missing)",
                l.RootItemId,
                l.Mode,
                l.CreatedUtc))
            .OrderBy(l => l.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.CreatorName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AdminOverviewDto(_injectionStatus.State, _injectionStatus.Message, links);
    }

    private static bool IsSupported(BaseItem item) => item is Movie or Episode or Season or Series;

    private User? GetCurrentUser()
    {
        // API keys authenticate with an empty user id; they are not users.
        var claim = User.FindFirst(UserIdClaimType)?.Value;
        return Guid.TryParse(claim, out var id) && !id.Equals(Guid.Empty) ? _userManager.GetUserById(id) : null;
    }

    private User? GetEnabledUser(Guid userId)
    {
        if (userId.Equals(Guid.Empty))
        {
            return null;
        }

        var user = _userManager.GetUserById(userId);
        return user is null || user.HasPermission(PermissionKind.IsDisabled) ? null : user;
    }

    private BaseItem? GetVisibleItem(Guid itemId, User user)
    {
        // GetItemById(id, user) checks library access and parental rules; BaseItem.IsVisible does not.
        return itemId.Equals(Guid.Empty) ? null : _libraryManager.GetItemById<BaseItem>(itemId, user);
    }

    private LinkDto? ToDto(SyncLink link, Guid me)
    {
        var isCreator = link.CreatorUserId.Equals(me);
        var partner = _userManager.GetUserById(isCreator ? link.PartnerUserId : link.CreatorUserId);
        return partner is null
            ? null
            : new LinkDto(link.Id, partner.Id, partner.Username, link.Mode, isCreator, LinkRules.CanRemove(link, me), link.CreatedUtc);
    }

    private Func<SyncLink, bool> BuildStaleCheck()
    {
        var userIds = _userManager.GetUsers().Select(u => u.Id).ToHashSet();
        return l => !userIds.Contains(l.CreatorUserId) || !userIds.Contains(l.PartnerUserId);
    }
}
