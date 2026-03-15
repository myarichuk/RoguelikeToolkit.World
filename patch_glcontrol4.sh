#!/bin/bash
sed -i 's/_plateLayer.Generate();/var pipeline = new WorldGenerationPipeline(); pipeline.AddStage(new TectonicPlateGenerationStage(_plateLayer.SeedCount)); pipeline.Execute(_map);/' src/RoguelikeToolkit.World.App/GlControl.cs
sed -i 's/_localLayer.Generate();/var pipeline2 = new WorldGenerationPipeline(); pipeline2.AddStage(new LocalMapGenerationStage()); pipeline2.Execute(_map);/' src/RoguelikeToolkit.World.App/GlControl.cs
