#!/bin/bash
sed -i 's/Assert.Throws<UnauthorizedAccessException>(() =>/Assert.Throws<UnauthorizedAccessException>(() => { var store = new WorldDataStore(1, traversalPath); store.RegisterLayer<DummyData>(); store.Allocate(); }/g' tests/RoguelikeToolkit.World.Core.Tests/SecurityTests.cs
sed -i 's/Assert.Throws<UnauthorizedAccessException>(() =>/Assert.Throws<UnauthorizedAccessException>(() => { var store = new WorldDataStore(1, absolutePath); store.RegisterLayer<DummyData>(); store.Allocate(); }/g' tests/RoguelikeToolkit.World.Core.Tests/SecurityTests.cs
