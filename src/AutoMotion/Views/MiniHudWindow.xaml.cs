using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoMotion.Models;

namespace AutoMotion.Views
{
    /// <summary>
    /// Rainmeter tarzı küçük, sürüklenebilir, her zaman üstte kalan istatistik göstergesi.
    /// Ana pencereden bağımsız bir mini widget olarak çalışır.
    /// </summary>
    public partial class MiniHudWindow : Window
    {
        public MiniHudWindow()
        {
            InitializeComponent();

            RootBorder.LayoutTransform = hudScale;
            PreviewMouseMove += Hud_EdgeMouseMove;
            PreviewMouseLeftButtonDown += Hud_EdgeMouseDown;
            PreviewMouseLeftButtonUp += Hud_EdgeMouseUp;
        }

        // ───────────────────────────── KENARDAN BOYUTLANDIRMA ─────────────────────────────
        // Ana penceredeki panellerle aynı davranış (bkz. MainWindow.DetectEdgeAt): mini HUD de
        // kenarlarından/köşelerinden tutulup büyütülüp küçültülebilir. Pencere SizeToContent ile
        // içeriğine göre boyutlandığından, içeriği ölçeklemek pencereyi de büyütür.

        private const double EdgeGrab = 8;
        private const double MinScale = 0.6, MaxScale = 2.2;

        private readonly ScaleTransform hudScale = new(1, 1);
        private bool hudResizing;
        private bool hudResizeHorizontal;
        private Point hudResizeStart;
        private double hudScaleAtStart;

        private bool NearEdge(Point p, out bool horizontal)
        {
            double w = RootBorder.ActualWidth * hudScale.ScaleX;
            double h = RootBorder.ActualHeight * hudScale.ScaleY;

            bool left = p.X <= EdgeGrab, right = p.X >= w - EdgeGrab;
            bool top = p.Y <= EdgeGrab, bottom = p.Y >= h - EdgeGrab;

            horizontal = left || right;
            return left || right || top || bottom;
        }

        private void Hud_EdgeMouseMove(object sender, MouseEventArgs e)
        {
            if (hudResizing)
            {
                var now = e.GetPosition(this);
                double delta = hudResizeHorizontal ? now.X - hudResizeStart.X : now.Y - hudResizeStart.Y;
                double basis = hudResizeHorizontal ? RootBorder.ActualWidth : RootBorder.ActualHeight;
                if (basis <= 0) return;

                double s = System.Math.Clamp(hudScaleAtStart + delta / basis, MinScale, MaxScale);
                hudScale.ScaleX = s;
                hudScale.ScaleY = s;
                e.Handled = true;
                return;
            }

            bool near = NearEdge(e.GetPosition(this), out bool horiz);
            Cursor = near ? (horiz ? Cursors.SizeWE : Cursors.SizeNS) : null;
        }

        private void Hud_EdgeMouseDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(this);
            if (!NearEdge(p, out bool horiz)) return; // kenarda değilse pencere sürüklemeye kalsın

            hudResizing = true;
            hudResizeHorizontal = horiz;
            hudResizeStart = p;
            hudScaleAtStart = hudScale.ScaleX;
            CaptureMouse();
            e.Handled = true;
        }

        private void Hud_EdgeMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!hudResizing) return;
            hudResizing = false;
            ReleaseMouseCapture();
            Cursor = null;
            e.Handled = true;
        }

        public void UpdateStats(int vehicleCount, int money, double mileageKm)
        {
            VehicleCountTxt.Text = vehicleCount.ToString();
            MoneyTxt.Text = $"${money}";
            MileageTxt.Text = $"{(int)mileageKm} km";
        }

        /// <summary>
        /// Tüm araçların konumunu, sanal masaüstü (veya tek ekran) sınırlarını mini-harita
        /// kutusuna ölçekleyerek küçük noktalar halinde çizer - özellikle çoklu-monitör modunda
        /// hangi aracın hangi ekranda olduğunu görmek için kullanışlıdır.
        /// </summary>
        public void UpdateMiniMap(IEnumerable<Vehicle> vehicles, double worldWidth, double worldHeight)
        {
            MiniMapCanvas.Children.Clear();
            if (worldWidth <= 0 || worldHeight <= 0) return;

            double mapW = MiniMapCanvas.Width;
            double mapH = MiniMapCanvas.Height;
            double scale = System.Math.Min(mapW / worldWidth, mapH / worldHeight);
            double offsetX = (mapW - worldWidth * scale) / 2;
            double offsetY = (mapH - worldHeight * scale) / 2;

            foreach (var vehicle in vehicles)
            {
                var dot = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = new SolidColorBrush(Color.FromRgb(0x3D, 0xA9, 0xFC))
                };
                Canvas.SetLeft(dot, offsetX + vehicle.X * scale - 2.5);
                Canvas.SetTop(dot, offsetY + vehicle.Y * scale - 2.5);
                MiniMapCanvas.Children.Add(dot);
            }
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseHudBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
