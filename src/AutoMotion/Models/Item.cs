using System;
using System.Collections.Generic;

namespace AutoMotion.Models
{
    /// <summary>
    /// Temel Item sınıfı
    /// </summary>
    public abstract class Item
    {
        public string ItemType { get; protected set; }
        public string Name { get; protected set; }
        public string Description { get; protected set; }
        public string Icon { get; protected set; }
        public int Quantity { get; set; } = 1;

        /// <summary>Borsa tipi yakıt fiyat dalgalanması gibi dış sistemlerin fiyatı ayarlayabilmesi
        /// için setter'ı public bırakıldı - normal eşyalar için değişmez, sadece Fuel için kullanılır.</summary>
        public int Cost { get; set; }

        protected Item(string itemType, string name, string description, string icon)
        {
            ItemType = itemType;
            Name = name;
            Description = description;
            Icon = icon;
        }

        public abstract bool Use(Vehicle vehicle);

        public virtual ItemData Serialize()
        {
            return new ItemData { ItemType = ItemType, SubType = "", Quantity = Quantity };
        }

        public virtual void Deserialize(ItemData data)
        {
            Quantity = data.Quantity;
        }
    }

    /// <summary>
    /// Yakıt sistemi
    /// </summary>
    public class Fuel : Item
    {
        public string FuelType { get; }
        public double FuelAmount { get; }

        public Fuel(string fuelType = "regular") : base("fuel", "", "", "")
        {
            var fuelTypes = new Dictionary<string, (string name, string icon, double amount, int cost)>
            {
                { "regular", ("⛽ Normal Yakıt", "⛽", 50, 10) },
                { "premium", ("🏁 Premium Yakıt", "🏁", 100, 25) },
                { "turbo", ("⚡ Turbo Yakıt", "⚡", 150, 50) }
            };

            if (fuelTypes.TryGetValue(fuelType, out var config))
            {
                FuelType = fuelType;
                Name = config.name;
                Icon = config.icon;
                FuelAmount = config.amount;
                Cost = config.cost;
                Description = $"{config.amount} yakıt birimi";
            }
        }

        public override bool Use(Vehicle vehicle)
        {
            if (vehicle.Fuel >= vehicle.MaxFuel)
                return false;

            vehicle.Fuel = Math.Min(vehicle.MaxFuel, vehicle.Fuel + FuelAmount);
            return true;
        }

        public override ItemData Serialize()
        {
            var data = base.Serialize();
            data.SubType = FuelType;
            return data;
        }
    }

    /// <summary>
    /// Onarım sistemi
    /// </summary>
    public class RepairKit : Item
    {
        public string RepairType { get; }
        public int HealAmount { get; }

        public RepairKit(string repairType = "standard") : base("repair", "", "", "")
        {
            var repairTypes = new Dictionary<string, (string name, string icon, int heal, int cost)>
            {
                { "standard", ("🔧 Standart Onarım", "🔧", 30, 30) },
                { "advanced", ("⚙️ İleri Onarım", "⚙️", 70, 70) },
                { "full", ("🛠️ Tam Onarım", "🛠️", 100, 150) }
            };

            if (repairTypes.TryGetValue(repairType, out var config))
            {
                RepairType = repairType;
                Name = config.name;
                Icon = config.icon;
                HealAmount = config.heal;
                Cost = config.cost;
                Description = $"{config.heal} HP tamir";
            }
        }

        public override bool Use(Vehicle vehicle)
        {
            // Onarım artık HP'nin yanı sıra lastik aşınmasını da sıfırlar ("bakıma girdi" hissi) -
            // HP zaten doluyken bile aşınmış lastikler varsa kullanım anlamlı sayılır.
            bool didSomething = false;

            if (vehicle.HP < vehicle.MaxHP)
            {
                vehicle.HP = Math.Min(vehicle.MaxHP, vehicle.HP + HealAmount);
                didSomething = true;
            }

            if (vehicle.TireWear > 0)
            {
                vehicle.TireWear = 0;
                didSomething = true;
            }

            return didSomething;
        }

        public override ItemData Serialize()
        {
            var data = base.Serialize();
            data.SubType = RepairType;
            return data;
        }
    }

    /// <summary>
    /// Upgrade sistemi
    /// </summary>
    public class Upgrade : Item
    {
        public string UpgradeType { get; }
        public string Effect { get; }
        public double Bonus { get; }

        public Upgrade(string upgradeType = "speed") : base("upgrade", "", "", "")
        {
            var upgradeTypes = new Dictionary<string, (string name, string icon, string effect, double bonus, int cost)>
            {
                { "speed", ("🚀 Hız Upgradı", "🚀", "speed", 0.2, 100) },
                { "acceleration", ("⚡ Hızlanma Upgradı", "⚡", "acceleration", 0.15, 80) },
                { "efficiency", ("💚 Verimlilik Upgradı", "💚", "efficiency", 0.25, 60) },
                { "durability", ("💪 Dayanıklılık Upgradı", "💪", "durability", 0.3, 90) }
            };

            if (upgradeTypes.TryGetValue(upgradeType, out var config))
            {
                UpgradeType = upgradeType;
                Name = config.name;
                Icon = config.icon;
                Effect = config.effect;
                Bonus = config.bonus;
                Cost = config.cost;
                Description = $"{config.effect} +{(config.bonus * 100):F0}%";
            }
        }

        public override bool Use(Vehicle vehicle)
        {
            if (vehicle.Upgrades.Contains(UpgradeType))
                return false;

            vehicle.Upgrades.Add(UpgradeType);

            switch (Effect)
            {
                case "speed":
                    vehicle.MaxSpeed *= (1 + Bonus);
                    break;
                case "acceleration":
                    vehicle.Acceleration *= (1 + Bonus);
                    break;
                case "efficiency":
                    vehicle.FuelConsumption *= (1 - Bonus);
                    break;
                case "durability":
                    vehicle.MaxHP = (int)(vehicle.MaxHP * (1 + Bonus));
                    break;
            }

            return true;
        }

        public override ItemData Serialize()
        {
            var data = base.Serialize();
            data.SubType = UpgradeType;
            return data;
        }
    }

    /// <summary>
    /// Dekorasyon sistemi
    /// </summary>
    public class Decoration : Item
    {
        public string DecorationType { get; }

        public Decoration(string decorationType = "paint") : base("decoration", "", "", "")
        {
            var decorationTypes = new Dictionary<string, (string name, string icon, int cost)>
            {
                { "paint", ("🎨 Boya", "🎨", 20) },
                { "sticker", ("✨ Sticker", "✨", 15) },
                { "spoiler", ("🏎️ Spoiler", "🏎️", 50) },
                { "lights", ("💡 LED Işık", "💡", 40) }
            };

            if (decorationTypes.TryGetValue(decorationType, out var config))
            {
                DecorationType = decorationType;
                Name = config.name;
                Icon = config.icon;
                Cost = config.cost;
                Description = "Görünüş iyileştirmesi";
            }
        }

        public override bool Use(Vehicle vehicle)
        {
            if (vehicle.Decorations.Contains(DecorationType))
                return false;

            vehicle.Decorations.Add(DecorationType);
            return true;
        }

        public override ItemData Serialize()
        {
            var data = base.Serialize();
            data.SubType = DecorationType;
            return data;
        }
    }

    /// <summary>
    /// Item Factory - Item oluşturma
    /// </summary>
    public static class ItemFactory
    {
        public static Item CreateItem(string itemType, string subType = "standard")
        {
            return itemType switch
            {
                "fuel" => new Fuel(subType),
                "repair" => new RepairKit(subType),
                "upgrade" => new Upgrade(subType),
                "decoration" => new Decoration(subType),
                _ => null
            };
        }
    }

    public class ItemData
    {
        public string ItemType { get; set; }
        public string SubType { get; set; }
        public int Quantity { get; set; }
    }
}
