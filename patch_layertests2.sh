#!/bin/bash
sed -i '/map.RegisterLayer(layer);/a \        map.DataStore.Allocate();' tests/RoguelikeToolkit.World.Core.Tests/LayerTests.cs
