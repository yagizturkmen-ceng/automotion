using System.Runtime.CompilerServices;
using System.Windows;

// Test projesi, yalnızca testler için açılmış iç (internal) yardımcıları görebilsin - ör.
// Loc.LoadForTest, diske dokunmadan sözlük besleyerek çeviri çözümlemesini test etmeyi sağlar.
[assembly: InternalsVisibleTo("AutoMotion.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
