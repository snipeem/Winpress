global using System;
global using System.IO;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Collections.ObjectModel;

// ── WPF type aliases ──────────────────────────────────────────────────────────
// These prevent CS0104 ambiguous-reference errors when UseWindowsForms=true,
// since System.Windows.Forms and System.Windows share many type names.
global using ListViewItem    = System.Windows.Controls.ListViewItem;
global using TreeViewItem    = System.Windows.Controls.TreeViewItem;
global using MenuItem        = System.Windows.Controls.MenuItem;
global using ContextMenu     = System.Windows.Controls.ContextMenu;
global using Button          = System.Windows.Controls.Button;
global using Label           = System.Windows.Controls.Label;
global using TextBox         = System.Windows.Controls.TextBox;
global using CheckBox        = System.Windows.Controls.CheckBox;
global using RadioButton     = System.Windows.Controls.RadioButton;
global using Panel           = System.Windows.Controls.Panel;
global using Control         = System.Windows.Controls.Control;
global using Image           = System.Windows.Controls.Image;
global using Cursor          = System.Windows.Input.Cursor;
global using ToolTip         = System.Windows.Controls.ToolTip;
global using Point           = System.Windows.Point;
global using Size            = System.Windows.Size;
global using Rectangle       = System.Windows.Rect;
global using Color           = System.Windows.Media.Color;
global using Font            = System.Windows.Media.FontFamily;
global using MouseEventArgs  = System.Windows.Input.MouseEventArgs;
global using KeyEventArgs    = System.Windows.Input.KeyEventArgs;
global using DragEventArgs   = System.Windows.DragEventArgs;
global using MessageBox      = System.Windows.MessageBox;
global using DataObject      = System.Windows.DataObject;
global using DataFormats     = System.Windows.DataFormats;
global using DragDropEffects = System.Windows.DragDropEffects;
