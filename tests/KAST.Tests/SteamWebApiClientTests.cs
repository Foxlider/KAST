using System.Net;
using System.Text;
using KAST.Infrastructure.Services;
using KAST.Infrastructure.Steam;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests;

public class SteamWebApiClientTests
{
    [Fact]
    public async Task GetPublishedFileDetailsBatchAsync_EmptyIds_ReturnsEmptyWithoutHttpCall()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new HttpClient(handler);
        var sut = new SteamWebApiClient(client, Substitute.For<ILogger<SteamWebApiClient>>(), new OutputSanitizer());

        var result = await sut.GetPublishedFileDetailsBatchAsync([]);

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetPublishedFileDetailsBatchAsync_ParsesAndMapsResponse()
    {
        const string json = """
        {
          "response": {
            "publishedfiledetails": [
              {
                "publishedfileid": "333310405",
                "result": 1,
                "creator": "76561198000000000",
                "creator_appid": 0,
                "consumer_appid": 107410,
                "title": "Small Mod",
                "description": "Desc",
                "time_updated": 1715600000,
                "preview_url": "https://cdn/image.jpg",
                "file_size": "2048",
                "hcontent_file": "987654321",
                "subscriptions": 42,
                "tags": [ { "tag": "Mod" }, { "tag": "Infantry" } ]
              },
              {
                "publishedfileid": "999",
                "result": 9,
                "title": "Ignored"
              }
            ]
          }
        }
        """;

        HttpRequestMessage? seenRequest = null;
        string? seenFormBody = null;
        var handler = new RecordingHandler((req, _) =>
        {
            seenRequest = req;
          seenFormBody = req.Content is null ? null : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var sut = new SteamWebApiClient(client, Substitute.For<ILogger<SteamWebApiClient>>(), new OutputSanitizer());

        var result = await sut.GetPublishedFileDetailsBatchAsync([333310405]);

        Assert.Single(result);
        var item = result[0];
        Assert.Equal(333310405, item.WorkshopId);
        Assert.Equal("Small Mod", item.Name);
        Assert.Equal(107410u, item.ConsumerAppId);
        Assert.Equal(987654321ul, item.ManifestId);
        Assert.Equal(2048, item.SizeBytes);
        Assert.Equal(42, item.Subscriptions);
        Assert.Equal(2, item.Tags.Count);

        Assert.NotNull(seenRequest);
        Assert.Equal(HttpMethod.Post, seenRequest!.Method);
        Assert.Contains("GetPublishedFileDetails/v1", seenRequest.RequestUri!.ToString());

        Assert.NotNull(seenFormBody);
        Assert.Contains("itemcount=1", seenFormBody);
        Assert.Contains("publishedfileids%5B0%5D=333310405", seenFormBody);
    }

    [Fact]
    public async Task GetPublishedFileDetailsAsync_NoValidResult_ReturnsNull()
    {
        const string json = """
        {
          "response": {
            "publishedfiledetails": [
              { "publishedfileid": "123", "result": 8 }
            ]
          }
        }
        """;

        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));

        var client = new HttpClient(handler);
        var sut = new SteamWebApiClient(client, Substitute.For<ILogger<SteamWebApiClient>>(), new OutputSanitizer());

        var result = await sut.GetPublishedFileDetailsAsync(123);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPublishedFileDetailsBatchAsync_NonSuccessStatus_Throws()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var client = new HttpClient(handler);
        var sut = new SteamWebApiClient(client, Substitute.For<ILogger<SteamWebApiClient>>(), new OutputSanitizer());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.GetPublishedFileDetailsBatchAsync([333310405]));
    }

    [Fact]
    public async Task DiscoverCdnServerAsync_PicksHttpsServerAndPrefersVHost()
    {
        const string json = """
        {
          "response": {
            "servers": [
              { "host": "cdn1.steamcontent.com", "vhost": "vcdn1", "https_support": "mandatory" },
              { "host": "cdn2.steamcontent.com", "https_support": "optional" }
            ]
          }
        }
        """;

        var handler = new RecordingHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("GetServersForSteamPipe", req.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(handler);
        var sut = new SteamWebApiClient(client, Substitute.For<ILogger<SteamWebApiClient>>(), new OutputSanitizer());

        var host = await sut.DiscoverCdnServerAsync();

        Assert.Equal("vcdn1", host);
    }

    [Fact]
    public async Task DiscoverCdnServerAsync_OnHttpFailure_ReturnsNull()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("boom"));
        var client = new HttpClient(handler);
        var sut = new SteamWebApiClient(client, Substitute.For<ILogger<SteamWebApiClient>>(), new OutputSanitizer());

        var host = await sut.DiscoverCdnServerAsync();

        Assert.Null(host);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _onSend;

        public int CallCount { get; private set; }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> onSend)
        {
            _onSend = onSend;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return await _onSend(request, cancellationToken);
        }
    }
}
