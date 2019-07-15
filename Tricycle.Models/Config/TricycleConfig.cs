﻿using System.Collections.Generic;

namespace Tricycle.Models.Config
{
    public class TricycleConfig
    {
        public VideoConfig Video { get; set; }
        public AudioConfig Audio { get; set; }
        public IDictionary<ContainerFormat, string> DefaultFileExtensions { get; set; }
    }
}