﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tricycle.Diagnostics;
using Tricycle.Diagnostics.Utilities;
using Tricycle.IO;
using Tricycle.Media.FFmpeg.Models.Config;
using Tricycle.Models;
using Tricycle.Models.Media;

namespace Tricycle.Media.FFmpeg
{
    public class CropDetector : ICropDetector
    {
        const int SAMPLE_COUNT = 5;
        static readonly TimeSpan MAX_SEEK_TIME = TimeSpan.FromMinutes(5);

        readonly string _ffmpegFileName;
        readonly IProcessRunner _processRunner;
        readonly IProcessUtility _processUtility;
        readonly IConfigManager<FFmpegConfig> _configManager;
        readonly TimeSpan _timeout;

        public CropDetector(string ffmpegFileName,
                            IProcessRunner processRunner,
                            IProcessUtility processUtility,
                            IConfigManager<FFmpegConfig> configManager)
            : this(ffmpegFileName, processRunner, processUtility, configManager, TimeSpan.FromSeconds(30))
        {

        }

        public CropDetector(string ffmpegFileName,
                            IProcessRunner processRunner,
                            IProcessUtility processUtility,
                            IConfigManager<FFmpegConfig> configManager,
                            TimeSpan timeout)
        {
            _ffmpegFileName = ffmpegFileName;
            _processRunner = processRunner;
            _processUtility = processUtility;
            _configManager = configManager;
            _timeout = timeout;
        }

        public async Task<CropParameters> Detect(MediaInfo mediaInfo)
        {
            if (mediaInfo == null)
            {
                throw new ArgumentNullException(nameof(mediaInfo));
            }
            if (string.IsNullOrWhiteSpace(mediaInfo.FileName))
            {
                throw new ArgumentException($"{nameof(mediaInfo)}.FileName must not be empty or whitespace.", nameof(mediaInfo));
            }
            if (mediaInfo.Duration <= TimeSpan.Zero)
			{
				throw new ArgumentException($"{nameof(mediaInfo)}.Duration is invalid.", nameof(mediaInfo));
			}

			CropParameters result = null;
            IEnumerable<double> positions = GetSeekSeconds(mediaInfo.Duration);
            var escapedFileName = _processUtility.EscapeFilePath(mediaInfo.FileName);
            FFmpegConfig config = _configManager.Config;
            string options = string.Empty;

            if (!string.IsNullOrWhiteSpace(config?.Video?.CropDetectOptions))
            {
                options = "=" + config.Video.CropDetectOptions;
            }

            var lockTarget = new object();
            int? minX = null, minY = null, maxWidth = null, maxHeight = null;

            var tasks = positions.Select(async seconds =>
            {
                var arguments = $"-hide_banner -ss {seconds:0.###} -i {escapedFileName} -frames:vf 2 -vf cropdetect{options} -f null -";

                try
                {
                    var processResult = await _processRunner.Run(_ffmpegFileName, arguments, _timeout);

                    //The crop detection data is written to standard error.
                    if (!string.IsNullOrWhiteSpace(processResult.ErrorData))
                    {
                        var crop = Parse(processResult.ErrorData);

                        if (crop != null)
                        {
                            lock (lockTarget)
                            {
                                minX = minX.HasValue ? Math.Min(crop.Start.X, minX.Value) : crop.Start.X;
                                minY = minY.HasValue ? Math.Min(crop.Start.Y, minY.Value) : crop.Start.Y;
                                maxWidth = maxWidth.HasValue ? Math.Max(crop.Size.Width, maxWidth.Value) : crop.Size.Width;
                                maxHeight = maxHeight.HasValue ? Math.Max(crop.Size.Height, maxHeight.Value) : crop.Size.Height;
                            }
                        }
                    }
                }
                catch (ArgumentException ex)
                {
                    Debug.WriteLine(ex);
                }
                catch (InvalidOperationException ex)
                {
                    Debug.WriteLine(ex);
                }
            });

            await Task.WhenAll(tasks);

            if (minX.HasValue && minY.HasValue && maxWidth.HasValue && maxHeight.HasValue)
            {
                result = new CropParameters()
                {
                    Start = new Coordinate<int>(minX.Value, minY.Value),
                    Size = new Dimensions(maxWidth.Value, maxHeight.Value)
                };
            }

            return result;
        }

        IEnumerable<double> GetSeekSeconds(TimeSpan duration)
        {
            double seconds = duration.TotalSeconds / 2;

            if (TimeSpan.FromSeconds(seconds) > MAX_SEEK_TIME)
            {
                seconds = MAX_SEEK_TIME.TotalSeconds;
            }

            return Enumerable.Range(1, SAMPLE_COUNT).Select(x => seconds / SAMPLE_COUNT * x);
        }

        CropParameters Parse(string outputData)
        {
            CropParameters result = null;
            int x, y, width, height;
            var match = Regex.Match(outputData,
                $"crop=(?<{nameof(width)}>\\d+):(?<{nameof(height)}>\\d+):(?<{nameof(x)}>\\d+):(?<{nameof(y)}>\\d+)");

            if (match.Success &&
                int.TryParse(match.Groups[nameof(x)].Value, out x) &&
                int.TryParse(match.Groups[nameof(y)].Value, out y) &&
                int.TryParse(match.Groups[nameof(width)].Value, out width) &&
                int.TryParse(match.Groups[nameof(height)].Value, out height))
            {
                result = new CropParameters()
                {
                    Start = new Coordinate<int>(x, y),
                    Size = new Dimensions(width, height)
                };
            }

            return result;
        }
    }
}
