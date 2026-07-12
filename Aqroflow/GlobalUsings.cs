// Resolve ambiguous types when UseWindowsForms=true alongside WPF.
// Prefer WPF types by default; WinForms types are qualified in TrayIconManager.
global using Application = System.Windows.Application;
global using Color = System.Windows.Media.Color;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using Point = System.Windows.Point;
global using Rectangle = System.Windows.Shapes.Rectangle;
global using Button = System.Windows.Controls.Button;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Clipboard = System.Windows.Clipboard;
