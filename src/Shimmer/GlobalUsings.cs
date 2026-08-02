// Shimmer enables BOTH WPF (UseWPF) and WinForms (UseWindowsForms) — WPF for the
// pool windows, WinForms only for the tray NotifyIcon and cursor position. With
// ImplicitUsings on, that pulls in both System.Windows.* and System.Windows.Forms.*
// as global namespaces, so a bare type name like `Button` or `Point` becomes
// ambiguous.
//
// These global alias usings pin every conflicting *simple* name to its WPF type.
// WinForms types are always reached explicitly in this project via the `Forms.`
// and `Drawing.` aliases (see App.xaml.cs), so pinning the bare names to WPF is
// safe across the whole assembly and keeps the rest of the code clean.

global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Point = System.Windows.Point;
global using Button = System.Windows.Controls.Button;
global using TextBox = System.Windows.Controls.TextBox;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using Color = System.Windows.Media.Color;
global using Brushes = System.Windows.Media.Brushes;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using Image = System.Windows.Controls.Image;
global using Cursors = System.Windows.Input.Cursors;
global using DataObject = System.Windows.DataObject;
global using DragDrop = System.Windows.DragDrop;
global using DragDropEffects = System.Windows.DragDropEffects;
global using DragEventArgs = System.Windows.DragEventArgs;
global using GiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
global using Orientation = System.Windows.Controls.Orientation;
global using Rectangle = System.Windows.Shapes.Rectangle;
