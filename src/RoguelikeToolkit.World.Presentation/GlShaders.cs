namespace RoguelikeToolkit.World.Presentation;

/// <summary>
/// Visualizer GLSL sources. Desktop GL gets <c>#version 330 core</c> shaders
/// with explicit attribute locations; OpenGL ES (e.g. Avalonia's ANGLE
/// backend on Windows, which exposes an ES 2.0-style context) gets
/// GLSL ES 1.00-compatible shaders (<c>attribute</c>/<c>varying</c>,
/// <c>gl_FragColor</c>, caller-bound attribute locations) that also compile
/// on ES 3.x contexts. Both variants implement the same lighting and
/// barycentric hex-edge math.
/// </summary>
public static class GlShaders
{
    /// <summary>Attribute locations used by the mesh VBO layout.</summary>
    public static readonly (int Location, string Name)[] Attributes =
    {
        (0, "aPos"),
        (1, "aNormal"),
        (2, "aBary"),
        (3, "aColor"),
    };

    public readonly record struct Selection(
        string VertexSource,
        string FragmentSource,
        bool BindAttributeLocations)
    {
        /// <summary>Short label used in diagnostics, e.g. "desktop" or "GLES".</summary>
        public string Name => BindAttributeLocations ? "GLES" : "desktop";
    }

    public static Selection Select(bool isGles) => isGles ? ForGles() : ForDesktop();

    public static Selection ForDesktop() => new(DesktopVertex, DesktopFragment, false);

    public static Selection ForGles() => new(GlesVertex, GlesFragment, true);

    private const string DesktopVertex = @"
            #version 330 core
            layout (location = 0) in vec3 aPos;
            layout (location = 1) in vec3 aNormal;
            layout (location = 2) in vec3 aBary;
            layout (location = 3) in vec3 aColor;

            out vec3 FragPos;
            out vec3 Normal;
            out vec3 Barycentric;
            out vec3 VertexColor;
            out float vIsSelectedVertex;

            uniform mat4 uMvpMatrix;
            uniform mat4 uModelMatrix;
            uniform vec3 uSelectedHexCenter;

            void main()
            {
                gl_Position = uMvpMatrix * vec4(aPos, 1.0);
                FragPos = vec3(uModelMatrix * vec4(aPos, 1.0));

                // For a sphere, the normal is just the position
                // Also, no non-uniform scaling, so we can just use the model matrix
                Normal = mat3(uModelMatrix) * aNormal;

                Barycentric = aBary;
                VertexColor = aColor;

                float dist = distance(aPos, uSelectedHexCenter);
                if (dist < 0.001) {
                    vIsSelectedVertex = 1.0;
                } else {
                    vIsSelectedVertex = 0.0;
                }
            }
        ";

    private const string DesktopFragment = @"
            #version 330 core
            #extension GL_OES_standard_derivatives : enable

            in vec3 FragPos;
            in vec3 Normal;
            in vec3 Barycentric;
            in vec3 VertexColor;
            in float vIsSelectedVertex;

            out vec4 FragColor;

            uniform int uShowPlates;
            uniform int uShowHexes;
            uniform int uTerrainMode;

            void main()
            {
                // Simple directional light
                vec3 norm = normalize(Normal);
                vec3 lightDir = normalize(vec3(1.0, 1.0, 1.0));

                // Ambient + diffuse
                float ambient = 0.2;
                float diff = max(dot(norm, lightDir), 0.0);
                float lightIntensity = ambient + diff * 0.8;

                // Base color
                vec3 baseColor = uShowPlates == 1 ? VertexColor : vec3(0.3, 0.3, 0.35);
                baseColor *= lightIntensity;

                // Highlight selected hex
                float maxBary = max(max(Barycentric.x, Barycentric.y), Barycentric.z);
                bool isFragmentInSelectedHex = (vIsSelectedVertex > 0.5 && Barycentric.x == maxBary) ||
                                               (vIsSelectedVertex > 0.5 && Barycentric.y == maxBary) ||
                                               (vIsSelectedVertex > 0.5 && Barycentric.z == maxBary);

                if (vIsSelectedVertex >= maxBary - 0.0001)
                {
                    baseColor = mix(baseColor, vec3(1.0, 1.0, 0.0), 0.5); // Highlight with yellow
                }

                float edgeFactor = 1.0;

                if (uTerrainMode == 1) {
                    // Fine canopy/rock grain so jungles and ranges read as texture,
                    // not flat fills. No hex or triangle edges in terrain view.
                    float g = fract(sin(dot(floor(FragPos * 220.0), vec3(12.9898, 78.233, 37.719))) * 43758.5453);
                    baseColor *= 0.94 + 0.12 * g;
                    edgeFactor = 1.0;
                }
                else if (uShowHexes == 1) {
                    float b1, b2, b3;
                    if (Barycentric.x > Barycentric.y) {
                        if (Barycentric.x > Barycentric.z) { b1 = Barycentric.x; b2 = max(Barycentric.y, Barycentric.z); }
                        else { b1 = Barycentric.z; b2 = Barycentric.x; }
                    } else {
                        if (Barycentric.y > Barycentric.z) { b1 = Barycentric.y; b2 = max(Barycentric.x, Barycentric.z); }
                        else { b1 = Barycentric.z; b2 = Barycentric.y; }
                    }

                    float val = b1 - b2;
                    float d = fwidth(val);
                    edgeFactor = smoothstep(0.0, d * 1.5, val);
                } else {
                    vec3 d = fwidth(Barycentric);
                    vec3 a3 = smoothstep(vec3(0.0), d * 1.5, Barycentric);
                    edgeFactor = min(min(a3.x, a3.y), a3.z);
                }

                vec3 edgeColor = vec3(0.6, 0.6, 0.6);

                vec3 finalColor = mix(edgeColor, baseColor, edgeFactor);

                FragColor = vec4(finalColor, 1.0);
            }
        ";

    private const string GlesVertex = @"
            precision highp float;
            attribute vec3 aPos;
            attribute vec3 aNormal;
            attribute vec3 aBary;
            attribute vec3 aColor;

            varying vec3 FragPos;
            varying vec3 Normal;
            varying vec3 Barycentric;
            varying vec3 VertexColor;
            varying float vIsSelectedVertex;

            uniform mat4 uMvpMatrix;
            uniform mat4 uModelMatrix;
            uniform vec3 uSelectedHexCenter;

            void main()
            {
                gl_Position = uMvpMatrix * vec4(aPos, 1.0);
                FragPos = vec3(uModelMatrix * vec4(aPos, 1.0));

                // For a sphere, the normal is just the position
                // Also, no non-uniform scaling, so we can just use the model matrix
                Normal = mat3(uModelMatrix) * aNormal;

                Barycentric = aBary;
                VertexColor = aColor;

                float dist = distance(aPos, uSelectedHexCenter);
                if (dist < 0.001) {
                    vIsSelectedVertex = 1.0;
                } else {
                    vIsSelectedVertex = 0.0;
                }
            }
        ";

    private const string GlesFragment = @"
            #ifdef GL_OES_standard_derivatives
            #extension GL_OES_standard_derivatives : enable
            #endif
            precision mediump float;

            varying vec3 FragPos;
            varying vec3 Normal;
            varying vec3 Barycentric;
            varying vec3 VertexColor;
            varying float vIsSelectedVertex;

            uniform int uShowPlates;
            uniform int uShowHexes;
            uniform int uTerrainMode;

            void main()
            {
                // Simple directional light
                vec3 norm = normalize(Normal);
                vec3 lightDir = normalize(vec3(1.0, 1.0, 1.0));

                // Ambient + diffuse
                float ambient = 0.2;
                float diff = max(dot(norm, lightDir), 0.0);
                float lightIntensity = ambient + diff * 0.8;

                // Base color
                vec3 baseColor = uShowPlates == 1 ? VertexColor : vec3(0.3, 0.3, 0.35);
                baseColor *= lightIntensity;

                // Highlight selected hex
                float maxBary = max(max(Barycentric.x, Barycentric.y), Barycentric.z);
                bool isFragmentInSelectedHex = (vIsSelectedVertex > 0.5 && Barycentric.x == maxBary) ||
                                               (vIsSelectedVertex > 0.5 && Barycentric.y == maxBary) ||
                                               (vIsSelectedVertex > 0.5 && Barycentric.z == maxBary);

                if (vIsSelectedVertex >= maxBary - 0.0001)
                {
                    baseColor = mix(baseColor, vec3(1.0, 1.0, 0.0), 0.5); // Highlight with yellow
                }

                float edgeFactor = 1.0;

                if (uTerrainMode == 1) {
                    // Fine canopy/rock grain so jungles and ranges read as texture,
                    // not flat fills. No hex or triangle edges in terrain view.
                    float g = fract(sin(dot(floor(FragPos * 220.0), vec3(12.9898, 78.233, 37.719))) * 43758.5453);
                    baseColor *= 0.94 + 0.12 * g;
                    edgeFactor = 1.0;
                }
                else if (uShowHexes == 1) {
                    float b1, b2, b3;
                    if (Barycentric.x > Barycentric.y) {
                        if (Barycentric.x > Barycentric.z) { b1 = Barycentric.x; b2 = max(Barycentric.y, Barycentric.z); }
                        else { b1 = Barycentric.z; b2 = Barycentric.x; }
                    } else {
                        if (Barycentric.y > Barycentric.z) { b1 = Barycentric.y; b2 = max(Barycentric.x, Barycentric.z); }
                        else { b1 = Barycentric.z; b2 = Barycentric.y; }
                    }

                    float val = b1 - b2;
                    float d = fwidth(val);
                    edgeFactor = smoothstep(0.0, d * 1.5, val);
                } else {
                    vec3 d = fwidth(Barycentric);
                    vec3 a3 = smoothstep(vec3(0.0), d * 1.5, Barycentric);
                    edgeFactor = min(min(a3.x, a3.y), a3.z);
                }

                vec3 edgeColor = vec3(0.6, 0.6, 0.6);

                vec3 finalColor = mix(edgeColor, baseColor, edgeFactor);

                gl_FragColor = vec4(finalColor, 1.0);
            }
        ";
}
