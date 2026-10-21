using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ProgressSync.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ProgressSync.Tests;

public class IndexTransformerTests
{
    private const string Html = "<html><head></head><body><div id=\"reactRoot\"></div></BODY></html>";

    [Fact]
    public void Transform_InsertsScriptBeforeClosingBody()
    {
        var result = IndexTransformer.Transform(new PatchPayload { Contents = Html });

        var script = "<script defer data-progress-sync src=\"../ProgressSync/ClientScript?v=" + IndexTransformer.ScriptVersion + "\"></script>";
        Assert.Contains(script + "</BODY>", result, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Transform_IsIdempotent()
    {
        var once = IndexTransformer.Transform(new PatchPayload { Contents = Html });
        var twice = IndexTransformer.Transform(new PatchPayload { Contents = once });

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Transform_DoctypeDocument_IsTransformed()
    {
        var result = IndexTransformer.Transform(new PatchPayload { Contents = "\uFEFF <!DOCTYPE html><html><body></body></html>" });

        Assert.Contains("data-progress-sync", result, System.StringComparison.Ordinal);
    }

    [Theory]
    // jellyfin-web chunks whose names match the "index.html" regex (e.g. itemDetails-index-html.*.chunk.js)
    [InlineData("\"use strict\";(self.webpackChunk=self.webpackChunk||[]).push([[76331],{1:e=>{e.exports='<div class=\"x\"></div>'}}]);")]
    [InlineData("(self.webpackChunk=self.webpackChunk||[]).push([[1],{1:e=>{e.exports='<html><body></body></html>'}}]);")]
    [InlineData("<div>fragment without body</div>")]
    [InlineData("<html><head></head></html>")]
    public void Transform_NonHtmlDocumentsOrMissingBody_AreUnchanged(string contents)
    {
        Assert.Equal(contents, IndexTransformer.Transform(new PatchPayload { Contents = contents }));
    }

    [Fact]
    public void Transform_EmptyPayload_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, IndexTransformer.Transform(new PatchPayload()));
        Assert.Equal(string.Empty, IndexTransformer.Transform(null!));
    }

    [Fact]
    public async Task WebInjectionService_WithoutFileTransformation_ReportsMissing()
    {
        var status = new WebInjectionStatus();
        var service = new WebInjectionService(status, NullLogger<WebInjectionService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(InjectionState.FileTransformationMissing, status.State);
    }
}
