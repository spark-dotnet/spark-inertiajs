using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using InertiaCore.Extensions;
using InertiaCore.Models;
using InertiaCore.Ssr;
using InertiaCore.Utils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace InertiaCore;

internal interface IResponseFactory
{
    public Response Render(string component, object? props = null);
    public Task<IHtmlContent> Head(dynamic model);
    public Task<IHtmlContent> Html(dynamic model);
    public void Version(object? version);
    public string? GetVersion();
    public LocationResult Location(string url);
    public void Share(string key, object? value);
    public void Share(IDictionary<string, object?> data);
    public LazyProp Lazy(Func<object?> callback);
}

internal class ResponseFactory : IResponseFactory
{
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly IGateway _gateway;
    private readonly IOptions<InertiaOptions> _options;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _host;

    private object? _version;

    public ResponseFactory(IHttpContextAccessor contextAccessor, IGateway gateway, IOptions<InertiaOptions> options, IConfiguration config, IWebHostEnvironment host) =>
        (_contextAccessor, _gateway, _options, _config, _host) = (contextAccessor, gateway, options, config, host);

    public Response Render(string component, object? props = null)
    {
        props ??= new { };

        return new Response(component, props, _options.Value.RootView, GetVersion());
    }

    public async Task<IHtmlContent> Head(dynamic model)
    {
        if (!_options.Value.SsrEnabled) return new HtmlString("");

        var context = _contextAccessor.HttpContext!;

        var response = context.Features.Get<SsrResponse>();
        response ??= await _gateway.Dispatch(model, _options.Value.SsrUrl);

        if (response == null) return new HtmlString("");

        context.Features.Set(response);
        return response.GetHead();
    }

    public async Task<IHtmlContent> Html(dynamic model)
    {
        if (_options.Value.SsrEnabled)
        {
            var context = _contextAccessor.HttpContext!;

            var response = context.Features.Get<SsrResponse>();
            response ??= await _gateway.Dispatch(model, _options.Value.SsrUrl);

            if (response != null)
            {
                context.Features.Set(response);
                return response.GetBody();
            }
        }

        var data = JsonSerializer.Serialize(model,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            });

        var encoded = WebUtility.HtmlEncode(data);

        return new HtmlString($"<div id=\"app\" data-page=\"{encoded}\"></div>");
    }

    public IHtmlContent Scripts()
    {
        // test env
        if (_host.IsProduction())
        {
            var css = $@"<script src=""~/{_config.GetSection("Vite:Build:OutputDir").Value}/app.js""></script>";
            return new HtmlString(css);
        }
        else
        {
            var css = $@"<script type=""module"" src=""http://localhost:{_config.GetSection("Vite:Port").Value}/Resources/Js/app.js""></script>";
            return new HtmlString(css);
        }
    }

    public IHtmlContent Css()
    {
        // test env
        if (_host.IsProduction())
        {
            var css = $@"<link rel=""stylesheet"" href=""~/{_config.GetSection("Vite:Build:OutputDir").Value}/app.css"" />";
            return new HtmlString(css);
        }
        return new HtmlString($"");
    }

    public void Version(object? version) => _version = version;

    public string? GetVersion() => _version switch
    {
        Func<string> func => func.Invoke(),
        string s => s,
        _ => null,
    };

    public LocationResult Location(string url) => new(url);

    public void Share(string key, object? value)
    {
        var context = _contextAccessor.HttpContext!;

        var sharedData = context.Features.Get<InertiaSharedData>();
        sharedData ??= new InertiaSharedData();
        sharedData.Set(key, value);

        context.Features.Set(sharedData);
    }

    public void Share(IDictionary<string, object?> data)
    {
        var context = _contextAccessor.HttpContext!;

        var sharedData = context.Features.Get<InertiaSharedData>();
        sharedData ??= new InertiaSharedData();
        sharedData.Merge(data);

        context.Features.Set(sharedData);
    }

    public LazyProp Lazy(Func<object?> callback) => new(callback);
}
