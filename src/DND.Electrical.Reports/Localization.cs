namespace DND.Electrical.Reports;

/// <summary>Report vocabulary in English and French (Arabic planned).</summary>
public static class L
{
    private static readonly Dictionary<string, (string en, string fr)> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bom"] = ("Bill of materials", "Nomenclature (liste de matériel)"),
        ["equipment"] = ("Equipment list", "Liste des équipements"),
        ["motors"] = ("Motor list", "Liste des moteurs"),
        ["loads"] = ("Electrical load list", "Bilan de puissance"),
        ["cables"] = ("Cable schedule", "Carnet de câbles"),
        ["cablecores"] = ("Cable connection report", "Raccordement des conducteurs de câbles"),
        ["wires"] = ("Wiring list (connections)", "Liste de filerie (connexions)"),
        ["terminals"] = ("Terminal diagram", "Plan de bornier"),
        ["plcio"] = ("PLC I/O list", "Liste des entrées/sorties automate"),
        ["xref"] = ("Component cross-reference report", "Rapport de références croisées"),
        ["revisions"] = ("Revision report", "Historique des révisions"),
        ["validation"] = ("Validation report", "Rapport de vérification"),
        ["calculations"] = ("Electrical calculation report", "Note de calcul électrique"),
        ["sheets"] = ("Drawing index", "Sommaire des folios"),
        ["summary"] = ("Project summary", "Synthèse du projet"),
        ["cabinet"] = ("Cabinet layout report", "Rapport d'implantation armoire"),
        ["specs"] = ("Technical specifications", "Spécifications techniques"),
        ["page"] = ("Page", "Page"),
        ["of"] = ("of", "sur"),
        ["generated"] = ("Generated", "Généré le"),
        ["project"] = ("Project", "Projet"),
        ["customer"] = ("Customer", "Client"),
        ["company"] = ("Company", "Société"),
        ["revision"] = ("Revision", "Révision"),
        ["engineer"] = ("Engineer", "Ingénieur"),
        ["status"] = ("Status", "Statut"),
        ["date"] = ("Date", "Date"),
        ["description"] = ("Description", "Description"),
        ["tag"] = ("Tag", "Repère"),
        ["function"] = ("Function", "Fonction"),
        ["manufacturer"] = ("Manufacturer", "Fabricant"),
        ["partnumber"] = ("Part number", "Référence"),
        ["quantity"] = ("Qty", "Qté"),
        ["unit"] = ("Unit", "Unité"),
        ["unitprice"] = ("Unit price", "Prix unitaire"),
        ["total"] = ("Total", "Total"),
        ["supplier"] = ("Supplier", "Fournisseur"),
        ["location"] = ("Location", "Emplacement"),
        ["sheet"] = ("Sheet", "Folio"),
        ["from"] = ("From", "Origine"),
        ["to"] = ("To", "Destination"),
        ["wire"] = ("Wire", "Fil"),
        ["potential"] = ("Potential", "Potentiel"),
        ["demo"] = ("DEMONSTRATION PROJECT – FICTIONAL DATA – NOT FOR CONSTRUCTION", "PROJET DE DÉMONSTRATION – DONNÉES FICTIVES – NE PAS UTILISER POUR LA RÉALISATION"),
        ["drawingset"] = ("Electrical drawing set", "Dossier de schémas électriques"),
        ["handover"] = ("Project handover documentation", "Dossier de remise d'ouvrage"),
        ["severity"] = ("Severity", "Gravité"),
        ["message"] = ("Message", "Message"),
        ["suggestion"] = ("Suggested correction", "Correction suggérée"),
        ["contents"] = ("Contents", "Sommaire"),
    };

    public static string T(string key, string language) =>
        Texts.TryGetValue(key, out var t) ? (language.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? t.fr : t.en) : key;
}
