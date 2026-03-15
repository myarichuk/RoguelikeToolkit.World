#!/bin/bash
sed -i '/_map.RegisterLayer(_localLayer);/a \            _map.DataStore.Allocate();' src/RoguelikeToolkit.World.App/GlControl.cs
