using System.Collections.Generic;
using System.Linq;
using AutoMotion.Models;

namespace AutoMotion.Services
{
    /// <summary>
    /// Kayıtlı eklentileri yönetir: aç/kapat, her karede güncelle.
    /// </summary>
    public class ExpansionManager
    {
        public List<Expansion> Expansions { get; } = new();

        public void Register(Expansion expansion)
        {
            Expansions.Add(expansion);
        }

        public void ToggleExpansion(string id, GameCore gameCore)
        {
            var expansion = Expansions.FirstOrDefault(e => e.Id == id);
            if (expansion == null) return;

            if (expansion.IsEnabled)
                expansion.Disable(gameCore);
            else
                expansion.Enable(gameCore);
        }

        public void UpdateAll(GameCore gameCore, int gameTime, double canvasWidth, double canvasHeight)
        {
            foreach (var expansion in Expansions)
                expansion.Tick(gameCore, gameTime, canvasWidth, canvasHeight);
        }
    }
}
