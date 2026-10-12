// WorldExports holds the current world in a static, exactly like the worker that hosts it in the browser.
// Test classes would otherwise run in parallel and Generate over each other's world.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
