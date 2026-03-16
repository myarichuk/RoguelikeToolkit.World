import re

with open("src/RoguelikeToolkit.World.App/MainWindow.axaml.cs", "r") as f:
    content = f.read()

# Need to properly remove TryPickHex and fix the syntax in MainWindow.axaml.cs. Let's just do it securely.
# Let's recreate MainWindow.axaml.cs to be exactly the original but without TryPickHex and without the deleted layers.
