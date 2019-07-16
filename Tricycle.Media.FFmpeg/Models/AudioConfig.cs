﻿using System.Collections.Generic;
using Tricycle.Models;

namespace Tricycle.Media.FFmpeg.Models
{
    public class AudioConfig
    {
        public IDictionary<AudioFormat, AudioCodec> Codecs { get; set; }
    }
}
