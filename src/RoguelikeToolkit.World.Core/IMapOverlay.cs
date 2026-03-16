using System;
using RoguelikeToolkit.World.Geometry;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

public interface IMapLayer<T> where T : unmanaged
{
    WorldDataStore Store { get; }
    T GetValue(GeoCoord coord);
}
