using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace RoguelikeToolkit.World.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Link button events
            var btnRotLeft = this.FindControl<Button>("BtnRotLeft");
            var btnRotRight = this.FindControl<Button>("BtnRotRight");
            var btnRotUp = this.FindControl<Button>("BtnRotUp");
            var btnRotDown = this.FindControl<Button>("BtnRotDown");
            var btnZoomIn = this.FindControl<Button>("BtnZoomIn");
            var btnZoomOut = this.FindControl<Button>("BtnZoomOut");
            var chkTogglePlates = this.FindControl<CheckBox>("ChkTogglePlates");
            var chkToggleHexes = this.FindControl<CheckBox>("ChkToggleHexes");
            var sldSeedCount = this.FindControl<Slider>("SldSeedCount");
            var sldRecursionLevel = this.FindControl<Slider>("SldRecursionLevel");
            var cmbProjection = this.FindControl<ComboBox>("CmbProjection");
            var numSeed = this.FindControl<NumericUpDown>("NumSeed");
            var btnRegen = this.FindControl<Button>("BtnRegen");
            var cmbViewMode = this.FindControl<ComboBox>("CmbViewMode");
            var sldHeight = this.FindControl<Slider>("SldHeight");
            var cmbColorMode = this.FindControl<ComboBox>("CmbColorMode");
            var txtStatus = this.FindControl<TextBlock>("TxtStatus");

            if (btnRotLeft != null) btnRotLeft.Click += (s, e) => { if (GlView.ProjectionMode == ProjectionType.Sphere) { GlView.Yaw -= 10f; GlView.RenderFrame(); } };
            if (btnRotRight != null) btnRotRight.Click += (s, e) => { if (GlView.ProjectionMode == ProjectionType.Sphere) { GlView.Yaw += 10f; GlView.RenderFrame(); } };
            if (btnRotUp != null) btnRotUp.Click += (s, e) => { if (GlView.ProjectionMode == ProjectionType.Sphere) { GlView.Pitch -= 10f; GlView.RenderFrame(); } };
            if (btnRotDown != null) btnRotDown.Click += (s, e) => { if (GlView.ProjectionMode == ProjectionType.Sphere) { GlView.Pitch += 10f; GlView.RenderFrame(); } };
            if (btnZoomIn != null) btnZoomIn.Click += (s, e) => { GlView.Distance -= 1f; GlView.RenderFrame(); };
            if (btnZoomOut != null) btnZoomOut.Click += (s, e) => { GlView.Distance += 1f; GlView.RenderFrame(); };

            if (cmbProjection != null)
            {
                cmbProjection.SelectionChanged += (s, e) =>
                {
                    if (cmbProjection.SelectedIndex >= 0)
                    {
                        GlView.SetProjectionMode((ProjectionType)cmbProjection.SelectedIndex);
                    }
                };
            }

            if (sldSeedCount != null)
            {
                sldSeedCount.ValueChanged += (s, e) =>
                {
                    GlView.SetPlateCount((int)e.NewValue);
                };
            }

            if (sldRecursionLevel != null)
            {
                sldRecursionLevel.ValueChanged += (s, e) =>
                {
                    GlView.SetRecursionLevel((int)e.NewValue);
                };
            }

            if (chkTogglePlates != null)
            {
                chkTogglePlates.IsCheckedChanged += (s, e) =>
                {
                    GlView.ShowPlates = chkTogglePlates.IsChecked ?? false;
                    GlView.RenderFrame();
                };
            }

            if (chkToggleHexes != null)
            {
                chkToggleHexes.IsCheckedChanged += (s, e) =>
                {
                    GlView.ShowHexes = chkToggleHexes.IsChecked ?? false;
                    GlView.RenderFrame();
                };
            }

            if (btnRegen != null)
            {
                btnRegen.Click += (s, e) =>
                {
                    int seed = (int)(numSeed?.Value ?? 42);
                    GlView.Regenerate(seed);
                };
            }

            if (cmbViewMode != null)
            {
                cmbViewMode.SelectionChanged += (s, e) =>
                {
                    if (cmbViewMode.SelectedIndex >= 0)
                    {
                        GlView.SetViewMode((ViewMode)cmbViewMode.SelectedIndex);
                    }
                };
            }

            if (sldHeight != null)
            {
                sldHeight.ValueChanged += (s, e) =>
                {
                    GlView.SetHeightScale((float)e.NewValue);
                };
            }

            if (cmbColorMode != null)
            {
                cmbColorMode.SelectionChanged += (s, e) =>
                {
                    if (cmbColorMode.SelectedIndex >= 0)
                    {
                        GlView.SetColorMode((ColorMode)cmbColorMode.SelectedIndex);
                    }
                };
            }

            void RefreshStatus() { if (txtStatus != null) txtStatus.Text = GlView.StatusText; }
            GlView.StatusChanged += (s, e) => RefreshStatus();
            GlView.OnDiagnostic = msg => { if (txtStatus != null) txtStatus.Text = msg; };
            RefreshStatus();

            WireAdaptivePanels(numSeed);

            // Auto-collapse panels when the window is too narrow to show them
            // alongside the 3D view (e.g. a small laptop screen). Layout only
            // changes when crossing a breakpoint, so explicit user toggles
            // are respected between resizes.
            this.SizeChanged += (s, e) => ApplyResponsiveLayout(e.NewSize.Width);

            // Global Key Down event
            this.KeyDown += MainWindow_KeyDown;
        }

        // Adaptive-layout breakpoint state. Tracked so auto-collapse only
        // fires when crossing a breakpoint instead of fighting explicit toggles.
        private bool _narrowControlsHidden;
        private bool _narrowInfoHidden;
        private const double ControlsBreakpointWidth = 900.0;
        private const double InfoBreakpointWidth = 640.0;

        private Avalonia.Point _lastMousePosition;
        private bool _isLeftDown;
        private bool _isRightDown;

        private void GlViewContainer_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(GlView);
            _lastMousePosition = point.Position;

            if (point.Properties.IsLeftButtonPressed) _isLeftDown = true;
            if (point.Properties.IsRightButtonPressed) _isRightDown = true;

            // Attempt to pick a hex (point is GlView-relative: TryPickHex works in GlView.Bounds space)
            if (GlView.TryPickHex(point.Position.X, point.Position.Y, out double lat, out double lon, out int tileIndex))
            {
                var txtLat = this.FindControl<TextBlock>("TxtHexLat");
                var txtLon = this.FindControl<TextBlock>("TxtHexLon");
                var txtIndex = this.FindControl<TextBlock>("TxtHexIndex");
                var txtPlate = this.FindControl<TextBlock>("TxtHexPlate");
                var txtElev = this.FindControl<TextBlock>("TxtHexElev");
                var txtBiome = this.FindControl<TextBlock>("TxtHexBiome");
                var txtDanger = this.FindControl<TextBlock>("TxtHexDanger");

                if (txtLat != null) txtLat.Text = $"Lat: {lat:F2}";
                if (txtLon != null) txtLon.Text = $"Lon: {lon:F2}";
                if (txtIndex != null) txtIndex.Text = $"Index: {tileIndex}";

                if (txtPlate != null && tileIndex >= 0)
                {
                    var plate = GlView.PlateLayer?.Store.GetRef<RoguelikeToolkit.World.Core.TectonicPlate>(tileIndex);
                    if (plate.HasValue)
                    {
                        txtPlate.Text = $"Plate ID: {plate.Value.Id}";
                    }
                    else
                    {
                        txtPlate.Text = $"Plate ID: --";
                    }
                }

                if (txtElev != null && tileIndex >= 0)
                {
                    var elev = GlView.ElevationLayer?.Store.GetRef<RoguelikeToolkit.World.Core.ElevationInfo>(tileIndex);
                    if (elev.HasValue)
                    {
                        txtElev.Text = $"Elev: {elev.Value.Height:F2}";
                    }
                    else
                    {
                        txtElev.Text = $"Elev: --";
                    }
                }

                if (txtBiome != null && txtDanger != null && tileIndex >= 0)
                {
                    var localInfo = GlView.LocalLayer?.Store.GetRef<RoguelikeToolkit.World.Core.LocalMapInfo>(tileIndex);
                    if (localInfo.HasValue)
                    {
                        txtBiome.Text = $"Biome: {localInfo.Value.Biome}";
                        txtDanger.Text = $"Danger: {localInfo.Value.DangerLevel}";
                    }
                    else
                    {
                        txtBiome.Text = $"Biome: --";
                        txtDanger.Text = $"Danger: --";
                    }
                }

                var txtFeatures = this.FindControl<TextBlock>("TxtHexFeatures");
                if (txtFeatures != null)
                    txtFeatures.Text = tileIndex >= 0 ? DescribeFeatures(tileIndex) : "Features: --";
            }
            else
            {
                var txtLat = this.FindControl<TextBlock>("TxtHexLat");
                var txtLon = this.FindControl<TextBlock>("TxtHexLon");
                var txtIndex = this.FindControl<TextBlock>("TxtHexIndex");
                var txtPlate = this.FindControl<TextBlock>("TxtHexPlate");
                var txtElev = this.FindControl<TextBlock>("TxtHexElev");
                var txtBiome = this.FindControl<TextBlock>("TxtHexBiome");
                var txtDanger = this.FindControl<TextBlock>("TxtHexDanger");

                if (txtLat != null) txtLat.Text = "Lat: --";
                if (txtLon != null) txtLon.Text = "Lon: --";
                if (txtIndex != null) txtIndex.Text = "Index: --";
                if (txtPlate != null) txtPlate.Text = "Plate ID: --";
                if (txtElev != null) txtElev.Text = "Elev: --";
                if (txtBiome != null) txtBiome.Text = "Biome: --";
                if (txtDanger != null) txtDanger.Text = "Danger: --";
                var txtFeatures = this.FindControl<TextBlock>("TxtHexFeatures");
                if (txtFeatures != null) txtFeatures.Text = "Features: --";
            }
        }

        private string DescribeFeatures(int tileIndex)
        {
            var f = GlView.GetTileFeatures(tileIndex);
            var parts = new System.Collections.Generic.List<string>();
            if (f.IsRiver)
            {
                string flow = f.UpstreamTile >= 0 ? $"#{f.UpstreamTile}" : f.RiverSource.ToString();
                string to = f.DownstreamTile >= 0 ? $"#{f.DownstreamTile}" : "sea/sink";
                parts.Add(f.RiverId >= 0 ? $"River #{f.RiverId} ({flow} → {to})" : $"River ({flow} → {to})");
            }
            if (f.BodyKind.HasValue)
                parts.Add(f.LakeDepth > 0f ? $"{f.BodyKind.Value} ({f.LakeDepth:F2})" : f.BodyKind.Value.ToString());
            if (f.IsGlacier) parts.Add("Glacier");
            if (f.InMountainRange) parts.Add("Range");
            if (f.InValley) parts.Add("Valley");
            if (f.InCanyon) parts.Add("Canyon");
            if (f.InPlaya) parts.Add("Playa");
            if (f.Deposits != null && f.Deposits.Length > 0)
                parts.Add(string.Join("+", f.Deposits));
            return parts.Count > 0 ? "Features: " + string.Join(" • ", parts) : "Features: --";
        }

        private void GlViewContainer_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left) _isLeftDown = false;
            if (e.InitialPressMouseButton == MouseButton.Right) _isRightDown = false;
        }

        private void GlViewContainer_PointerMoved(object sender, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint(GlView);

            if (_isLeftDown)
            {
                var deltaX = (float)(point.Position.X - _lastMousePosition.X);
                var deltaY = (float)(point.Position.Y - _lastMousePosition.Y);

                if (GlView.ProjectionMode == ProjectionType.Sphere)
                {
                    GlView.Yaw += deltaX * 0.5f;
                    GlView.Pitch += deltaY * 0.5f;

                    if (GlView.Pitch > 89.0f) GlView.Pitch = 89.0f;
                    if (GlView.Pitch < -89.0f) GlView.Pitch = -89.0f;
                }
                else
                {
                    float panSpeed = 0.005f * GlView.Distance;
                    GlView.PanX -= deltaX * panSpeed;
                    GlView.PanY += deltaY * panSpeed;
                }

                GlView.RenderFrame();
            }

            if (_isRightDown)
            {
                var deltaX = (float)(point.Position.X - _lastMousePosition.X);
                var deltaY = (float)(point.Position.Y - _lastMousePosition.Y);

                float panSpeed = 0.005f * GlView.Distance;
                GlView.PanX -= deltaX * panSpeed;
                GlView.PanY += deltaY * panSpeed;

                GlView.RenderFrame();
            }

            _lastMousePosition = point.Position;
        }

        private void GlViewContainer_PointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            GlView.Distance -= (float)e.Delta.Y * 0.5f;
            if (GlView.Distance < 1.1f) GlView.Distance = 1.1f;
            if (GlView.Distance > 20.0f) GlView.Distance = 20.0f;
            GlView.RenderFrame();
        }

        private void WireAdaptivePanels(NumericUpDown? numSeed)
        {
            var controlsPanel = this.FindControl<Border>("ControlsPanel");
            var infoPanel = this.FindControl<Border>("InfoPanel");
            var toggleControls = this.FindControl<ToggleButton>("BtnToggleControls");
            var toggleInfo = this.FindControl<ToggleButton>("BtnToggleInfo");

            if (toggleControls != null && controlsPanel != null)
                toggleControls.IsCheckedChanged += (s, e) => controlsPanel.IsVisible = toggleControls.IsChecked ?? false;
            if (toggleInfo != null && infoPanel != null)
                toggleInfo.IsCheckedChanged += (s, e) => infoPanel.IsVisible = toggleInfo.IsChecked ?? false;


            // The primary action stays reachable from the toolbar when the
            // side panel is hidden on small screens.
            var quickRegen = this.FindControl<Button>("BtnQuickRegen");
            if (quickRegen != null)
                quickRegen.Click += (s, e) => GlView.Regenerate((int)(numSeed?.Value ?? 42));
        }

        private void ApplyResponsiveLayout(double width)
        {
            bool hideControls = width < ControlsBreakpointWidth;
            if (hideControls != _narrowControlsHidden)
            {
                _narrowControlsHidden = hideControls;
                var toggle = this.FindControl<ToggleButton>("BtnToggleControls");
                if (toggle != null) toggle.IsChecked = !hideControls;
            }

            bool hideInfo = width < InfoBreakpointWidth;
            if (hideInfo != _narrowInfoHidden)
            {
                _narrowInfoHidden = hideInfo;
                var toggle = this.FindControl<ToggleButton>("BtnToggleInfo");
                if (toggle != null) toggle.IsChecked = !hideInfo;
            }
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                    if (GlView.ProjectionMode == ProjectionType.Sphere) GlView.Yaw -= 5f;
                    else GlView.PanX -= 0.1f * GlView.Distance;
                    break;
                case Key.Right:
                    if (GlView.ProjectionMode == ProjectionType.Sphere) GlView.Yaw += 5f;
                    else GlView.PanX += 0.1f * GlView.Distance;
                    break;
                case Key.Up:
                    if (GlView.ProjectionMode == ProjectionType.Sphere) GlView.Pitch -= 5f;
                    else GlView.PanY += 0.1f * GlView.Distance;
                    break;
                case Key.Down:
                    if (GlView.ProjectionMode == ProjectionType.Sphere) GlView.Pitch += 5f;
                    else GlView.PanY -= 0.1f * GlView.Distance;
                    break;
                case Key.Add:
                case Key.OemPlus:
                    GlView.Distance -= 0.5f;
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    GlView.Distance += 0.5f;
                    break;
            }
            GlView.RenderFrame();
        }
    }
}
