using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

public interface IMapLayer<T> where T : unmanaged
{
    HexSphereStore<T> Store { get; }
    void Generate();
    T GetValue(GeoCoord coord);
}