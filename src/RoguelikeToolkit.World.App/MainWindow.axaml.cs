using Avalonia.Controls;
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

            if (btnRotLeft != null) btnRotLeft.Click += (s, e) => { GlView.Yaw -= 10f; GlView.RenderFrame(); };
            if (btnRotRight != null) btnRotRight.Click += (s, e) => { GlView.Yaw += 10f; GlView.RenderFrame(); };
            if (btnRotUp != null) btnRotUp.Click += (s, e) => { GlView.Pitch -= 10f; GlView.RenderFrame(); };
            if (btnRotDown != null) btnRotDown.Click += (s, e) => { GlView.Pitch += 10f; GlView.RenderFrame(); };
            if (btnZoomIn != null) btnZoomIn.Click += (s, e) => { GlView.Distance -= 1f; GlView.RenderFrame(); };
            if (btnZoomOut != null) btnZoomOut.Click += (s, e) => { GlView.Distance += 1f; GlView.RenderFrame(); };

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

            // Global Key Down event
            this.KeyDown += MainWindow_KeyDown;
        }

        private Avalonia.Point _lastMousePosition;
        private bool _isLeftDown;
        private bool _isRightDown;

        private void GlViewContainer_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            _lastMousePosition = point.Position;

            if (point.Properties.IsLeftButtonPressed) _isLeftDown = true;
            if (point.Properties.IsRightButtonPressed) _isRightDown = true;

            // Attempt to pick a hex
            if (GlView.TryPickHex(point.Position.X, point.Position.Y, out double lat, out double lon, out int tileIndex))
            {
                var txtLat = this.FindControl<TextBlock>("TxtHexLat");
                var txtLon = this.FindControl<TextBlock>("TxtHexLon");
                var txtIndex = this.FindControl<TextBlock>("TxtHexIndex");
                var txtPlate = this.FindControl<TextBlock>("TxtHexPlate");
                var txtBiome = this.FindControl<TextBlock>("TxtHexBiome");
                var txtDanger = this.FindControl<TextBlock>("TxtHexDanger");

                if (txtLat != null) txtLat.Text = $"Lat: {lat:F2}";
                if (txtLon != null) txtLon.Text = $"Lon: {lon:F2}";
                if (txtIndex != null) txtIndex.Text = $"Index: {tileIndex}";

                if (txtPlate != null && tileIndex >= 0)
                {
                    var plate = GlView.PlateLayer?.Store[tileIndex];
                    if (plate.HasValue)
                    {
                        txtPlate.Text = $"Plate ID: {plate.Value.Id}";
                    }
                    else
                    {
                        txtPlate.Text = $"Plate ID: --";
                    }
                }

                if (txtBiome != null && txtDanger != null && tileIndex >= 0)
                {
                    var localInfo = GlView.LocalLayer?.Store[tileIndex];
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
            }
            else
            {
                var txtLat = this.FindControl<TextBlock>("TxtHexLat");
                var txtLon = this.FindControl<TextBlock>("TxtHexLon");
                var txtIndex = this.FindControl<TextBlock>("TxtHexIndex");
                var txtPlate = this.FindControl<TextBlock>("TxtHexPlate");
                var txtBiome = this.FindControl<TextBlock>("TxtHexBiome");
                var txtDanger = this.FindControl<TextBlock>("TxtHexDanger");

                if (txtLat != null) txtLat.Text = "Lat: --";
                if (txtLon != null) txtLon.Text = "Lon: --";
                if (txtIndex != null) txtIndex.Text = "Index: --";
                if (txtPlate != null) txtPlate.Text = "Plate ID: --";
                if (txtBiome != null) txtBiome.Text = "Biome: --";
                if (txtDanger != null) txtDanger.Text = "Danger: --";
            }
        }

        private void GlViewContainer_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left) _isLeftDown = false;
            if (e.InitialPressMouseButton == MouseButton.Right) _isRightDown = false;
        }

        private void GlViewContainer_PointerMoved(object sender, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint(this);

            if (_isLeftDown)
            {
                var deltaX = (float)(point.Position.X - _lastMousePosition.X);
                var deltaY = (float)(point.Position.Y - _lastMousePosition.Y);
                GlView.Yaw += deltaX * 0.5f;
                GlView.Pitch += deltaY * 0.5f;

                if (GlView.Pitch > 89.0f) GlView.Pitch = 89.0f;
                if (GlView.Pitch < -89.0f) GlView.Pitch = -89.0f;

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

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                    GlView.Yaw -= 5f;
                    break;
                case Key.Right:
                    GlView.Yaw += 5f;
                    break;
                case Key.Up:
                    GlView.Pitch -= 5f;
                    break;
                case Key.Down:
                    GlView.Pitch += 5f;
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
