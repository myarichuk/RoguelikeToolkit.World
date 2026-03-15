#!/bin/bash
sed -i '/void Generate();/d' src/RoguelikeToolkit.World.Core/IMapOverlay.cs
sed -i '/public void Generate()/,/^    }$/d' src/RoguelikeToolkit.World.Core/TectonicPlateLayer.cs
sed -i '/public void Generate()/,/^    }$/d' src/RoguelikeToolkit.World.Core/LocalMapLayer.cs
