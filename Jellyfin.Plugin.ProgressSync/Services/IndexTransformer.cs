using System;
using System.Globalization;

namespace Jellyfin.Plugin.ProgressSync.Services;

/// <summary>
/// Adds the client script to jellyfin-web's index.html. Invoked by the File Transformation plugin
/// through reflection, so the signature of <see cref="Transform"/> must not change.
/// </summary>
public static class IndexTransformer
{
    /// <summary>
    /// Attribute that marks the injected script tag.
    /// </summary>
    internal const string Marker = "data-progress-sync";

    /// <summary>
    /// Changes with every build, so browsers fetch the new script after an update.
    /// </summary>
    internal static readonly string ScriptVersion =
        typeof(IndexTransformer).Assembly.ManifestModule.ModuleVersionId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Inserts the script tag before the closing body tag of an HTML document. Anything else is
    /// returned unchanged: File Transformation also uses the "index.html" key as a regex, which
    /// matches jellyfin-web chunks such as "itemDetails-index-html.[hash].chunk.js".
    /// </summary>
    /// <param name="payload">The file contents as provided by File Transformation.</param>
    /// <returns>The transformed contents.</returns>
    public static string Transform(PatchPayload payload)
    {
        var html = payload?.Contents ?? string.Empty;
        if (!IsHtmlDocument(html) || html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return html;
        }

        // Relative URL: index.html is served from {BaseUrl}/web/, so this resolves to
        // {BaseUrl}/ProgressSync/ClientScript for any BaseUrl.
        var tag = $"<script defer {Marker} src=\"../ProgressSync/ClientScript?v={ScriptVersion}\"></script>";
        return html.Insert(index, tag);
    }

    private static bool IsHtmlDocument(string contents)
    {
        var start = contents.AsSpan().TrimStart('\uFEFF').TrimStart();
        return start.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }
}
