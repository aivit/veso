using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Subtitles;
using Microsoft.Extensions.Logging;
using MimeTypes = MediaBrowser.Model.Net.MimeTypes;

namespace MediaBrowser.Api.Subtitles
{
    [Route("/Videos/{Id}/Subtitles/{Index}", "DELETE", Summary = "Deletes an external subtitle file")]
    [Authenticated(Roles = "Admin")]
    public class DeleteSubtitle
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
        public Guid Id { get; set; }

        [ApiMember(Name = "Index", Description = "The subtitle stream index", IsRequired = true, DataType = "int", ParameterType = "path", Verb = "DELETE")]
        public int Index { get; set; }
    }

    [Route("/Items/{Id}/RemoteSearch/Subtitles/{Language}", "GET")]
    [Authenticated]
    public class SearchRemoteSubtitles : IReturn<RemoteSubtitleInfo[]>
    {
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public Guid Id { get; set; }

        [ApiMember(Name = "Language", Description = "Language", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Language { get; set; }

        public bool? IsPerfectMatch { get; set; }
    }

    [Route("/Items/{Id}/RemoteSearch/Subtitles/{SubtitleId}", "POST")]
    [Authenticated]
    public class DownloadRemoteSubtitles : IReturnVoid
    {
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public Guid Id { get; set; }

        [ApiMember(Name = "SubtitleId", Description = "SubtitleId", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string SubtitleId { get; set; }
    }

    [Route("/Providers/Subtitles/Subtitles/{Id}", "GET")]
    [Authenticated]
    public class GetRemoteSubtitles : IReturnVoid
    {
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }
    }

    [Route("/Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/Stream.{Format}", "GET", Summary = "Gets subtitles in a specified format.")]
    [Route("/Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/{StartPositionTicks}/Stream.{Format}", "GET", Summary = "Gets subtitles in a specified format.")]
    public class GetSubtitle
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public Guid Id { get; set; }

        [ApiMember(Name = "MediaSourceId", Description = "MediaSourceId", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string MediaSourceId { get; set; }

        [ApiMember(Name = "Index", Description = "The subtitle stream index", IsRequired = true, DataType = "int", ParameterType = "path", Verb = "GET")]
        public int Index { get; set; }

        [ApiMember(Name = "Format", Description = "Format", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Format { get; set; }

        [ApiMember(Name = "StartPositionTicks", Description = "StartPositionTicks", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public long StartPositionTicks { get; set; }

        [ApiMember(Name = "EndPositionTicks", Description = "EndPositionTicks", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public long? EndPositionTicks { get; set; }

        [ApiMember(Name = "CopyTimestamps", Description = "CopyTimestamps", IsRequired = false, DataType = "bool", ParameterType = "query", Verb = "GET")]
        public bool CopyTimestamps { get; set; }

        public bool AddVttTimeMap { get; set; }
    }

    [Route("/Videos/{Id}/{MediaSourceId}/Subtitles/{Index}/subtitles.m3u8", "GET", Summary = "Gets an HLS subtitle playlist.")]
    [Authenticated]
    public class GetSubtitlePlaylist
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }

        [ApiMember(Name = "MediaSourceId", Description = "MediaSourceId", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string MediaSourceId { get; set; }

        [ApiMember(Name = "Index", Description = "The subtitle stream index", IsRequired = true, DataType = "int", ParameterType = "path", Verb = "GET")]
        public int Index { get; set; }

        [ApiMember(Name = "SegmentLength", Description = "The subtitle srgment length", IsRequired = true, DataType = "int", ParameterType = "query", Verb = "GET")]
        public int SegmentLength { get; set; }
    }

    [Route("/FallbackFont/Fonts", "GET", Summary = "Gets the fallback font list")]
    [Authenticated]
    public class GetFallbackFontList
    {
    }

    [Route("/FallbackFont/Fonts/{Name}", "GET", Summary = "Gets the fallback font file")]
    [Authenticated]
    public class GetFallbackFont
    {
        [ApiMember(Name = "Name", Description = "The font file name.", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "GET", AllowMultiple = true)]
        public string Name { get; set; }
    }

    public class SubtitleService : BaseApiService
    {
        private readonly IServerConfigurationManager _serverConfigurationManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ISubtitleManager _subtitleManager;
        private readonly ISubtitleEncoder _subtitleEncoder;
        private readonly IMediaSourceManager _mediaSourceManager;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly IAuthorizationContext _authContext;

        public SubtitleService(
            ILogger<SubtitleService> logger,
            IServerConfigurationManager serverConfigurationManager,
            IHttpResultFactory httpResultFactory,
            ILibraryManager libraryManager,
            ISubtitleManager subtitleManager,
            ISubtitleEncoder subtitleEncoder,
            IMediaSourceManager mediaSourceManager,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            IAuthorizationContext authContext)
            : base(logger, serverConfigurationManager, httpResultFactory)
        {
            _serverConfigurationManager = serverConfigurationManager;
            _libraryManager = libraryManager;
            _subtitleManager = subtitleManager;
            _subtitleEncoder = subtitleEncoder;
            _mediaSourceManager = mediaSourceManager;
            _providerManager = providerManager;
            _fileSystem = fileSystem;
            _authContext = authContext;
        }

        public async Task<object> Get(GetSubtitlePlaylist request)
        {
            var item = (Video)_libraryManager.GetItemById(new Guid(request.Id));

            var mediaSource = await _mediaSourceManager.GetMediaSource(item, request.MediaSourceId, null, false, CancellationToken.None).ConfigureAwait(false);

            var builder = new StringBuilder();

            var runtime = mediaSource.RunTimeTicks ?? -1;

            if (runtime <= 0)
            {
                throw new ArgumentException("HLS Subtitles are not supported for this media.");
            }

            var segmentLengthTicks = TimeSpan.FromSeconds(request.SegmentLength).Ticks;
            if (segmentLengthTicks <= 0)
            {
                throw new ArgumentException("segmentLength was not given, or it was given incorrectly. (It should be bigger than 0)");
            }

            builder.AppendLine("#EXTM3U");
            builder.AppendLine("#EXT-X-TARGETDURATION:" + request.SegmentLength.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("#EXT-X-VERSION:3");
            builder.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
            builder.AppendLine("#EXT-X-PLAYLIST-TYPE:VOD");

            long positionTicks = 0;

            var accessToken = _authContext.GetAuthorizationInfo(Request).Token;

            while (positionTicks < runtime)
            {
                var remaining = runtime - positionTicks;
                var lengthTicks = Math.Min(remaining, segmentLengthTicks);

                builder.AppendLine("#EXTINF:" + TimeSpan.FromTicks(lengthTicks).TotalSeconds.ToString(CultureInfo.InvariantCulture) + ",");

                var endPositionTicks = Math.Min(runtime, positionTicks + segmentLengthTicks);

                var url = string.Format("stream.vtt?CopyTimestamps=true&AddVttTimeMap=true&StartPositionTicks={0}&EndPositionTicks={1}&api_key={2}",
                    positionTicks.ToString(CultureInfo.InvariantCulture),
                    endPositionTicks.ToString(CultureInfo.InvariantCulture),
                    accessToken);

                builder.AppendLine(url);

                positionTicks += segmentLengthTicks;
            }

            builder.AppendLine("#EXT-X-ENDLIST");

            return ResultFactory.GetResult(Request, builder.ToString(), MimeTypes.GetMimeType("playlist.m3u8"), new Dictionary<string, string>());
        }

        public async Task<object> Get(GetSubtitle request)
        {
            if (string.Equals(request.Format, "js", StringComparison.OrdinalIgnoreCase))
            {
                request.Format = "json";
            }

            if (string.IsNullOrEmpty(request.Format))
            {
                var item = (Video)_libraryManager.GetItemById(request.Id);

                var idString = request.Id.ToString("N", CultureInfo.InvariantCulture);
                var mediaSource = _mediaSourceManager.GetStaticMediaSources(item, false, null)
                    .First(i => string.Equals(i.Id, request.MediaSourceId ?? idString));

                var subtitleStream = mediaSource.MediaStreams
                    .First(i => i.Type == MediaStreamType.Subtitle && i.Index == request.Index);

                return await ResultFactory.GetStaticFileResult(Request, subtitleStream.Path).ConfigureAwait(false);
            }

            if (string.Equals(request.Format, "vtt", StringComparison.OrdinalIgnoreCase) && request.AddVttTimeMap)
            {
                using var stream = await GetSubtitles(request).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                var text = reader.ReadToEnd();

                text = text.Replace("WEBVTT", "WEBVTT\nX-TIMESTAMP-MAP=MPEGTS:900000,LOCAL:00:00:00.000");

                return ResultFactory.GetResult(Request, text, MimeTypes.GetMimeType("file." + request.Format));
            }

            return ResultFactory.GetResult(Request, await GetSubtitles(request).ConfigureAwait(false), MimeTypes.GetMimeType("file." + request.Format));
        }

        private Task<Stream> GetSubtitles(GetSubtitle request)
        {
            var item = _libraryManager.GetItemById(request.Id);

            return _subtitleEncoder.GetSubtitles(item,
                request.MediaSourceId,
                request.Index,
                request.Format,
                request.StartPositionTicks,
                request.EndPositionTicks ?? 0,
                request.CopyTimestamps,
                CancellationToken.None);
        }

        public async Task<object> Get(SearchRemoteSubtitles request)
        {
            var video = (Video)_libraryManager.GetItemById(request.Id);

            return await _subtitleManager.SearchSubtitles(video, request.Language, request.IsPerfectMatch, CancellationToken.None).ConfigureAwait(false);
        }

        public Task Delete(DeleteSubtitle request)
        {
            var item = _libraryManager.GetItemById(request.Id);
            return _subtitleManager.DeleteSubtitles(item, request.Index);
        }

        public async Task<object> Get(GetRemoteSubtitles request)
        {
            var result = await _subtitleManager.GetRemoteSubtitles(request.Id, CancellationToken.None).ConfigureAwait(false);

            return ResultFactory.GetResult(Request, result.Stream, MimeTypes.GetMimeType("file." + result.Format));
        }

        public void Post(DownloadRemoteSubtitles request)
        {
            var video = (Video)_libraryManager.GetItemById(request.Id);

            Task.Run(async () =>
            {
                try
                {
                    await _subtitleManager.DownloadSubtitles(video, request.SubtitleId, CancellationToken.None)
                        .ConfigureAwait(false);

                    _providerManager.QueueRefresh(video.Id, new MetadataRefreshOptions(new DirectoryService(_fileSystem)), RefreshPriority.High);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error downloading subtitles");
                }
            });
        }

        public object Get(GetFallbackFontList request)
        {
            IEnumerable<FileSystemMetadata> fontFiles = Enumerable.Empty<FileSystemMetadata>();

            var encodingOptions = EncodingConfigurationExtensions.GetEncodingOptions(_serverConfigurationManager);
            var fallbackFontPath = encodingOptions.FallbackFontPath;

            if (!string.IsNullOrEmpty(fallbackFontPath))
            {
                try
                {
                    fontFiles = _fileSystem.GetFiles(fallbackFontPath, new[] { ".woff", ".woff2", ".ttf", ".otf" }, false, false);

                    var result = fontFiles.Select(i => new FontFile
                    {
                        Name = i.Name,
                        Size = i.Length,
                        DateCreated = _fileSystem.GetCreationTimeUtc(i),
                        DateModified = _fileSystem.GetLastWriteTimeUtc(i)
                    }).OrderBy(i => i.Size)
                        .ThenBy(i => i.Name)
                        .ThenByDescending(i => i.DateModified)
                        .ThenByDescending(i => i.DateCreated)
                        .ToArray();

                    // max total size 20M
                    var maxSize = 20971520;
                    var sizeCounter = 0L;
                    for (int i = 0; i < result.Length; i++)
                    {
                        sizeCounter += result[i].Size;
                        if (sizeCounter >= maxSize)
                        {
                            Logger.LogWarning("Some fonts will not be sent due to size limitations");
                            Array.Resize(ref result, i);
                            break;
                        }
                    }

                    return ToOptimizedResult(result);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error getting fallback font list");
                }
            }
            else
            {
                Logger.LogWarning("The path of fallback font folder has not been set");
                encodingOptions.EnableFallbackFont = false;
            }

            return ResultFactory.GetResult(Request, "[]", MediaTypeNames.Application.Json);
        }

        public async Task<object> Get(GetFallbackFont request)
        {
            var encodingOptions = EncodingConfigurationExtensions.GetEncodingOptions(_serverConfigurationManager);
            var fallbackFontPath = encodingOptions.FallbackFontPath;

            if (!string.IsNullOrEmpty(fallbackFontPath))
            {
                try
                {
                    // max single font size 10M
                    var maxSize = 10485760;
                    var fontFile = _fileSystem.GetFiles(fallbackFontPath)
                        .First(i => string.Equals(i.Name, request.Name, StringComparison.OrdinalIgnoreCase));
                    var fileSize = fontFile?.Length;

                    if (fileSize != null && fileSize > 0)
                    {
                        Logger.LogDebug("Fallback font size is {0} Bytes", fileSize);

                        if (fileSize <= maxSize)
                        {
                            return await ResultFactory.GetStaticFileResult(Request, fontFile.FullName);
                        }

                        Logger.LogWarning("The selected font is too large. Maximum allowed size is 10 Megabytes");
                    }
                    else
                    {
                        Logger.LogWarning("The selected font is null or empty");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error reading fallback font");
                }
            }
            else
            {
                Logger.LogWarning("The path of fallback font folder has not been set");
                encodingOptions.EnableFallbackFont = false;
            }

            return ResultFactory.GetResult(Request, string.Empty, MediaTypeNames.Text.Plain);
        }
    }
}
