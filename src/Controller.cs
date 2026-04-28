using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Meilisearch;

[Route("meilisearch")]
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
public class Controller(MeilisearchClientHolder clientHolder, Indexer indexer) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult GetStatus()
    {
        return new JsonResult(new
        {
            meilisearch = clientHolder.Status,
            meilisearchOk = clientHolder.Ok,
            indexStatus = indexer.Status
        });
    }

    // TODO: These should be [HttpPost] for correctness, but changing them is a breaking change
    // for the config.html frontend which currently uses GET requests via ApiClient.get().

    [HttpGet("reconnect")]
    public async Task<ActionResult> Reconnect()
    {
        var plugin = Plugin.Instance;
        if (!clientHolder.Ok && plugin is not null)
            await plugin.TryCreateMeilisearchClient().ConfigureAwait(false);
        return GetStatus();
    }

    [HttpGet("reindex")]
    public async Task<ActionResult> Reindex()
    {
        await indexer.Index().ConfigureAwait(false);
        return GetStatus();
    }
}
