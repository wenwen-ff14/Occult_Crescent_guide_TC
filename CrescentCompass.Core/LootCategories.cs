namespace CrescentCompass.Core;

public enum LootCategory
{
    Minion, Orchestrion, Mount, Equipment, Appearance, Dye, Materia, Furnishing,
    Consumable, FieldNote, Card, Other,
}

public static class LootCategories
{
    public static LootCategory Classify(LootItem item)
    {
        // Unavailable and unmarketable items may have no search category in the TC catalog.
        var name = item.EnglishName;
        if (name.EndsWith(" Orchestrion Roll", StringComparison.OrdinalIgnoreCase)) return LootCategory.Orchestrion;
        if (name.StartsWith("Notes on ", StringComparison.OrdinalIgnoreCase)) return LootCategory.FieldNote;
        if (name.EndsWith(" Card", StringComparison.OrdinalIgnoreCase)) return LootCategory.Card;
        if (name.StartsWith("Modern Aesthetics - ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Ballroom Etiquette - ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("The Faces We Wear - ", StringComparison.OrdinalIgnoreCase)) return LootCategory.Appearance;
        if (item.SearchCategory is 0 or 90 && (name.EndsWith(" Horn", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(" Whistle", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(" Identification Key", StringComparison.OrdinalIgnoreCase) || item.Id == 26782)) return LootCategory.Mount;

        return item.SearchCategory switch
        {
            75 => LootCategory.Minion,
            80 => LootCategory.Orchestrion,
            31 or 33 or 35 or 36 or 37 => LootCategory.Equipment,
            54 => LootCategory.Dye,
            57 => LootCategory.Materia,
            56 or 67 or 68 or 69 or 70 or 71 => LootCategory.Furnishing,
            60 => LootCategory.Consumable,
            90 => LootCategory.Appearance,
            _ => LootCategory.Other,
        };
    }
}
