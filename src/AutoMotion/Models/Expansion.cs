namespace AutoMotion.Models
{
    /// <summary>
    /// Eklenti temel sınıfı - AutoMotion'a sonradan takılabilen özellikler
    /// (örn. Otomatik Trafik, Mağaza, Görevler) bundan türetilir.
    /// </summary>
    public abstract class Expansion
    {
        public string Id { get; }
        // Ad/açıklama dil değişimini izlesin diye her okunuşta sözlükten çözülür; kurucuya verilen
        // Türkçe metin yalnızca sözlükte karşılığı yoksa kullanılan yedektir.
        private readonly string fallbackName;
        private readonly string fallbackDescription;
        public string Name => Services.Loc.TOrDefault($"expansion.{Id}.name", fallbackName);
        public string Description => Services.Loc.TOrDefault($"expansion.{Id}.desc", fallbackDescription);
        public string Icon { get; }
        public bool IsEnabled { get; private set; }

        public string StatusIcon => IsEnabled ? "🟢" : "⚪";
        public string StatusLabel => Services.Loc.T(IsEnabled ? "ui.state_on" : "ui.state_off");

        protected Expansion(string id, string name, string description, string icon)
        {
            Id = id;
            fallbackName = name;
            fallbackDescription = description;
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
