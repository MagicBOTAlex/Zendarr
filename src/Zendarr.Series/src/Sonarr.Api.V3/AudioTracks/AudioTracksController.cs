using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.AudioTracks;
using NzbDrone.Core.Movies;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.Subsystem;

namespace Sonarr.Api.V3.AudioTracks
{
    [V3ApiController("audio-tracks")]
    [V5ApiController("audio-tracks")]
    [AppSubsystem(AppSubsystem.Series)]
    [AppSubsystem(AppSubsystem.Movies)]
    public class AudioTracksController : ControllerBase
    {
        private readonly IAudioTrackService _audioTrackService;
        private readonly IMovieFileService _movieFileService;
        private readonly IMovieService _movieService;

        public AudioTracksController(IAudioTrackService audioTrackService,
                                     IMovieFileService movieFileService,
                                     IMovieService movieService)
        {
            _audioTrackService = audioTrackService;
            _movieFileService = movieFileService;
            _movieService = movieService;
        }

        [HttpGet("movie/{movieFileId:int}")]
        [Produces("application/json")]
        public Ok<AudioTrackListResource> GetMovieFileTracks(int movieFileId,
                                                              [FromQuery] string[] preferredLanguages,
                                                              [FromQuery] string orderBy,
                                                              [FromQuery] bool? descending,
                                                              [FromQuery] string defaultLanguage,
                                                              [FromQuery] int? defaultAudioIndex,
                                                              [FromQuery] bool? preserveDefaultWhenNoPreferred)
        {
            var resolved = ResolveMovieFile(movieFileId);
            var options = BuildOptions(preferredLanguages, orderBy, descending ?? false, defaultLanguage, defaultAudioIndex, preserveDefaultWhenNoPreferred ?? true);
            var result = _audioTrackService.GetTracks(resolved.Path, options);

            if (!result.Success)
            {
                throw new NotFoundException(result.Error);
            }

            return TypedResults.Ok(new AudioTrackListResource
            {
                MovieFileId = movieFileId,
                MovieId = resolved.Movie.Id,
                MovieTitle = resolved.Movie.Title,
                Path = resolved.Path,
                Container = result.ContainerFormat,
                Tracks = result.Tracks.ToResources(options.PreferredLanguages),
                OrderedTracks = result.Order.Tracks.ToResources(options.PreferredLanguages),
                CurrentDefaultAudioIndex = result.Tracks.FirstOrDefault(track => track.IsDefault)?.AudioIndex ?? -1,
                DefaultAudioIndex = result.Order.DefaultAudioIndex,
                DefaultLanguage = result.Order.DefaultLanguage,
                PreferredLanguageFound = result.Order.PreferredLanguageFound
            });
        }

        [HttpPost("movie/{movieFileId:int}/reorder")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public Ok<AudioTrackReorderResultResource> ReorderMovieFile(int movieFileId,
                                                                     [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AudioTrackReorderResource resource)
        {
            var resolved = ResolveMovieFile(movieFileId);
            var options = BuildOptions(resource?.PreferredLanguages, resource?.OrderBy, resource?.Descending ?? false, resource?.DefaultLanguage, resource?.DefaultAudioIndex, resource?.PreserveDefaultWhenNoPreferred ?? true);
            var outputPath = ValidateOutputPath(resource?.OutputPath, resolved.MovieFolder);

            var result = _audioTrackService.Reorder(new AudioTrackReorderRequest
            {
                SourcePath = resolved.Path,
                OutputPath = outputPath,
                OrderOptions = options,
                IncludeCopiedStreams = resource?.IncludeCopiedStreams ?? true,
                ReplaceOriginal = resource?.ReplaceOriginal ?? false,
                DryRun = resource?.DryRun ?? false
            });

            return TypedResults.Ok(new AudioTrackReorderResultResource
            {
                Success = result.Success,
                MovieFileId = movieFileId,
                SourcePath = result.SourcePath,
                OutputPath = result.OutputPath,
                BackupPath = result.BackupPath,
                Command = result.Command,
                Executed = result.Executed,
                ReplacedOriginal = result.ReplacedOriginal,
                OutputSize = result.OutputSize,
                Tracks = result.Order?.Tracks.ToResources(options.PreferredLanguages),
                DefaultAudioIndex = result.Order?.DefaultAudioIndex ?? -1,
                DefaultLanguage = result.Order?.DefaultLanguage,
                Error = result.Error
            });
        }

        [HttpPost("merge")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public Ok<AudioTrackMergeResultResource> MergeMovieFiles([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AudioTrackMergeResource resource)
        {
            if (resource == null)
            {
                throw new BadRequestException("Request body must be provided");
            }

            var baseFile = ResolveMovieFile(resource.BaseMovieFileId);
            var additionalIds = (resource.AdditionalMovieFileIds ?? new List<int>())
                .Where(id => id > 0 && id != resource.BaseMovieFileId)
                .Distinct()
                .ToList();

            var additionalFiles = additionalIds.Select(ResolveMovieFile).ToList();
            var options = BuildOptions(resource.PreferredLanguages, resource.OrderBy, resource.Descending, resource.DefaultLanguage, resource.DefaultAudioIndex, resource.PreserveDefaultWhenNoPreferred);
            var outputPath = ValidateOutputPath(resource.OutputPath, baseFile.MovieFolder);

            var result = _audioTrackService.Merge(new AudioTrackMergeRequest
            {
                BasePath = baseFile.Path,
                AdditionalPaths = additionalFiles.Select(file => file.Path).ToList(),
                OutputPath = outputPath,
                OrderOptions = options,
                IncludeBaseAudio = resource.IncludeBaseAudio,
                IncludeCandidateAudio = resource.IncludeCandidateAudio,
                IncludeCopiedStreams = resource.IncludeCopiedStreams,
                SkipDuplicateTracks = resource.SkipDuplicateTracks,
                ReplaceOriginal = resource.ReplaceOriginal,
                DryRun = resource.DryRun,
                SampleSeconds = resource.SampleSeconds,
                MinMatchScore = resource.MinScore,
                MaxSampleOffsetSeconds = resource.MaxSampleOffsetSeconds,
                SampleRate = resource.SampleRate,
                MaxSamples = resource.MaxSamples
            });

            var idsByPath = additionalFiles.ToDictionary(file => file.Path, file => file.MovieFileId, StringComparer.Ordinal);

            return TypedResults.Ok(new AudioTrackMergeResultResource
            {
                Success = result.Success,
                BaseMovieFileId = resource.BaseMovieFileId,
                BasePath = result.BasePath,
                OutputPath = result.OutputPath,
                BackupPath = result.BackupPath,
                Command = result.Command,
                Executed = result.Executed,
                ReplacedOriginal = result.ReplacedOriginal,
                OutputSize = result.OutputSize,
                Matches = result.Matches.Select(match => new AudioTrackMatchResource
                {
                    MovieFileId = idsByPath.TryGetValue(match.Path ?? string.Empty, out var id) ? id : 0,
                    Path = match.Path,
                    AudioIndex = match.AudioIndex,
                    Language = match.Language,
                    Title = match.Title,
                    Matched = match.Matched,
                    OffsetSeconds = match.OffsetSeconds,
                    Score = match.Score,
                    SampleCount = match.SampleCount,
                    MatchedSampleCount = match.MatchedSampleCount,
                    Error = match.Error,
                    Samples = match.Samples.Select(sample => new AudioSamplePointResource
                    {
                        PositionSeconds = sample.PositionSeconds,
                        OffsetSeconds = sample.OffsetSeconds,
                        Score = sample.Score,
                        Error = sample.Error
                    }).ToList(),
                    MergedTracks = match.MergedTracks.ToResources(options.PreferredLanguages)
                }).ToList(),
                Tracks = result.OutputTracks.ToResources(options.PreferredLanguages),
                Error = result.Error
            });
        }

        private ResolvedMovieFile ResolveMovieFile(int movieFileId)
        {
            if (movieFileId <= 0)
            {
                throw new BadRequestException("A valid movie file id is required");
            }

            var movieFile = _movieFileService.GetMovie(movieFileId);

            if (movieFile == null)
            {
                throw new NotFoundException($"Movie file {movieFileId} was not found");
            }

            var movie = _movieService.GetMovie(movieFile.MovieId);

            if (movie == null)
            {
                throw new NotFoundException($"Movie {movieFile.MovieId} was not found");
            }

            var path = movieFile.Path;

            if (path.IsNullOrWhiteSpace())
            {
                path = movieFile.RelativePath;
            }

            if (path.IsNullOrWhiteSpace())
            {
                throw new NotFoundException($"Movie file {movieFileId} has no path");
            }

            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(movie.Path, path);
            }

            path = Path.GetFullPath(path);

            var folder = movie.Path.IsNotNullOrWhiteSpace() ? movie.Path : Path.GetDirectoryName(path);

            return new ResolvedMovieFile
            {
                MovieFileId = movieFileId,
                Movie = movie,
                Path = path,
                MovieFolder = Path.GetFullPath(folder ?? string.Empty)
            };
        }

        private static AudioTrackOrderOptions BuildOptions(string[] preferredLanguages,
                                                           string orderBy,
                                                           bool descending,
                                                           string defaultLanguage,
                                                           int? defaultAudioIndex,
                                                           bool preserveDefaultWhenNoPreferred = true)
        {
            return new AudioTrackOrderOptions
            {
                PreferredLanguages = ParseLanguages(preferredLanguages),
                OrderBy = ParseOrderBy(orderBy),
                Descending = descending,
                DefaultLanguage = defaultLanguage,
                DefaultAudioIndex = defaultAudioIndex,
                PreserveDefaultWhenNoPreferred = preserveDefaultWhenNoPreferred
            };
        }

        private static List<string> ParseLanguages(string[] preferredLanguages)
        {
            var values = (preferredLanguages ?? Array.Empty<string>())
                .SelectMany(value => value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(value => value.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return values.Count > 0 ? values : new List<string> { "eng" };
        }

        private static AudioTrackOrderField ParseOrderBy(string orderBy)
        {
            if (orderBy.IsNullOrWhiteSpace())
            {
                return AudioTrackOrderField.Language;
            }

            return orderBy.Trim().ToLowerInvariant() switch
            {
                "language" or "lang" => AudioTrackOrderField.Language,
                "name" or "title" => AudioTrackOrderField.Name,
                "bitrate" => AudioTrackOrderField.Bitrate,
                "source" or "index" or "original" => AudioTrackOrderField.Source,
                _ => throw new BadRequestException($"Unknown orderBy '{orderBy}'")
            };
        }

        private static string ValidateOutputPath(string requested, string movieFolder)
        {
            if (requested.IsNullOrWhiteSpace())
            {
                return null;
            }

            var folder = Path.GetFullPath(movieFolder);
            var path = Path.IsPathRooted(requested)
                ? Path.GetFullPath(requested)
                : Path.GetFullPath(Path.Combine(folder, requested));

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var folderPrefix = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!path.StartsWith(folderPrefix, comparison) && !string.Equals(path, folder, comparison))
            {
                throw new BadRequestException("The output path must be inside the movie folder");
            }

            if (string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".mov", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".m4v", StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Audio-track remuxes must target an .mkv file so every audio codec can be copied");
            }

            if (string.IsNullOrWhiteSpace(Path.GetExtension(path)))
            {
                path += ".mkv";
            }

            return path;
        }

        private class ResolvedMovieFile
        {
            public int MovieFileId { get; set; }
            public Movie Movie { get; set; }
            public string Path { get; set; }
            public string MovieFolder { get; set; }
        }
    }
}
