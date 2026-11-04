using System;

namespace AutoMotion.Models
{
    public class Obstacle
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public double X { get; set; }
        public double Y { get; set; }
        public string Type { get; set; } // "traffic_light", "cargo_package", "delivery_point"
        public string Emoji { get; set; }
        public double Width { get; set; } = 32;
        public double Height { get; set; } = 32;
        public string State { get; set; } = ""; // "red", "yellow", "green", "idle", "held"
        public int Timer { get; set; } = 0;

        public Obstacle(double x, double y, string type)
        {
            X = x;
            Y = y;
            Type = type;
            InitializeObstacle();
        }

        private void InitializeObstacle()
        {
            if (Type == "traffic_light")
            {
                State = "red";
                Emoji = "🔴";
                Timer = 180; // 3 saniye kırmızı kalır (60fps varsayımıyla)
            }
            else if (Type == "cargo_package")
            {
                State = "idle";
                Emoji = "📦";
            }
            else if (Type == "delivery_point")
            {
                Emoji = "🏁";
            }
            else if (Type == "station")
            {
                State = "idle";
                Emoji = "⛽";
            }
            else if (Type == "cone")
            {
                State = "idle";
                Emoji = "🚧";
                Width = 22;
                Height = 22;
            }
            else if (Type == "police")
            {
                State = "red"; // trafik ışığı fizik mantığıyla aynı şekilde araçları durdurur
                Emoji = "🚓";
            }
        }

        public void Update()
        {
            if (Type == "traffic_light")
            {
                Timer--;
                if (Timer <= 0)
                {
                    if (State == "red")
                    {
                        State = "green";
                        Emoji = "🟢";
                        Timer = 240; // 4 saniye yeşil
                    }
                    else if (State == "green")
                    {
                        State = "yellow";
                        Emoji = "🟡";
                        Timer = 60; // 1 saniye sarı
                    }
                    else
                    {
                        State = "red";
                        Emoji = "🔴";
                        Timer = 180; // 3 saniye kırmızı
                    }
                }
            }
        }

        public bool IsPointInside(double px, double py)
        {
            double dx = px - X;
            double dy = py - Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            return distance < Math.Max(Width, Height);
        }

        public ObstacleData Serialize()
        {
            return new ObstacleData { X = X, Y = Y, Type = Type, State = State, Timer = Timer };
        }

        public void Deserialize(ObstacleData data)
        {
            // X/Y/Type zaten yapıcıda (constructor) verildi; burada sadece zamanla değişen
            // durumu (ör. trafik ışığının hangi renkte ve süre kaldığını) geri yüklüyoruz.
            State = data.State ?? State;
            Timer = data.Timer;
        }
    }

    public class ObstacleData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Type { get; set; }
        public string State { get; set; } = "";
        public int Timer { get; set; }
    }
}
