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

            if (btnRotLeft != null) btnRotLeft.Click += (s, e) => { GlView.Yaw -= 10f; GlView.RenderFrame(); };
            if (btnRotRight != null) btnRotRight.Click += (s, e) => { GlView.Yaw += 10f; GlView.RenderFrame(); };
            if (btnRotUp != null) btnRotUp.Click += (s, e) => { GlView.Pitch -= 10f; GlView.RenderFrame(); };
            if (btnRotDown != null) btnRotDown.Click += (s, e) => { GlView.Pitch += 10f; GlView.RenderFrame(); };
            if (btnZoomIn != null) btnZoomIn.Click += (s, e) => { GlView.Distance -= 1f; GlView.RenderFrame(); };
            if (btnZoomOut != null) btnZoomOut.Click += (s, e) => { GlView.Distance += 1f; GlView.RenderFrame(); };

            if (chkTogglePlates != null)
            {
                chkTogglePlates.IsCheckedChanged += (s, e) =>
                {
                    GlView.ShowPlates = chkTogglePlates.IsChecked ?? false;
                    GlView.RenderFrame();
                };
            }

            // Global Key Down event
            this.KeyDown += MainWindow_KeyDown;
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
