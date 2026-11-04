namespace AutoMotion.Models
{
    /// <summary>
    /// Eklenti temel sınıfı - AutoMotion'a sonradan takılabilen özellikler
    /// (örn. Otomatik Trafik, Mağaza, Görevler) bundan türetilir.
    /// </summary>
    public abstract class Expansion
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Icon { get; }
        public bool IsEnabled { get; private set; }

        public string StatusIcon => IsEnabled ? "🟢" : "⚪";
        public string StatusLabel => IsEnabled ? "Açık" : "Kapalı";

        protected Expansion(string id, string name, string description, string icon)
        {
            Id = id;
            Name = name;
            Description = description;
            Icon = icon;
        }

        public void Enable(GameCore gameCore)
        {
            if (IsEnabled) return;
            IsEnabled = true;
            OnEnable(gameCore);
        }

        public void Disable(GameCore gameCore)
        {
            if (!IsEnabled) return;
            IsEnabled = false;
            OnDisable(gameCore);
        }

        public void Tick(GameCore gameCore, int gameTime, double canvasWidth, double canvasHeight)
        {
            if (IsEnabled)
                OnUpdate(gameCore, gameTime, canvasWidth, canvasHeight);
        }

        protected virtual void OnEnable(GameCore gameCore) { }
        protected virtual void OnDisable(GameCore gameCore) { }
        protected virtual void OnUpdate(GameCore gameCore, int gameTime, double canvasWidth, double canvasHeight) { }
    }
}
