// WPF와 Windows Forms가 공존할 때 발생하는 타입 모호성을 전역 별칭으로 해결합니다.
global using Application = System.Windows.Application;
global using Clipboard    = System.Windows.Clipboard;
global using MessageBox   = System.Windows.MessageBox;
global using Button       = System.Windows.Controls.Button;
global using IDataObject  = System.Windows.IDataObject;
global using Timer        = System.Threading.Timer;
