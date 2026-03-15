#!/bin/bash
# Fix security tests in WorldDataStore: check path strictly on construction if we can, or on Allocate()
# Oh, we moved the file opening logic to Allocate() so the constructor doesn't throw anymore.
# We need to update the SecurityTests to call Allocate() to trigger the exception.
sed -i 's/using var store = new WorldDataStore(1, traversalPath);/using var store = new WorldDataStore(1, traversalPath);\n        store.RegisterLayer<DummyData>();\n        store.Allocate();/g' tests/RoguelikeToolkit.World.Core.Tests/SecurityTests.cs
sed -i 's/using var store = new WorldDataStore(1, absolutePath);/using var store = new WorldDataStore(1, absolutePath);\n        store.RegisterLayer<DummyData>();\n        store.Allocate();/g' tests/RoguelikeToolkit.World.Core.Tests/SecurityTests.cs

# Fix Discover_LoadsStagesInOrder test - it's failing because stages.Count >= 2 is False
# Wait, let's see why Discover() didn't find the stages.
# Ah, IWorldGeneratorStage, TectonicPlateGenerationStage, LocalMapGenerationStage are in RoguelikeToolkit.World.Core
# Assembly.GetExecutingAssembly() inside WorldGenerationPipeline.cs returns RoguelikeToolkit.World.Core.
# Let's verify stages count.
