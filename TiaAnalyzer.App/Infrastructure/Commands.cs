using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TiaAnalyzer.App.Infrastructure
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action execute;
        private readonly Func<bool> canExecute;
        public RelayCommand(Action execute, Func<bool> canExecute)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }
        public bool CanExecute(object parameter) { return canExecute(); }
        public void Execute(object parameter) { if (CanExecute(parameter)) execute(); }
        public event EventHandler CanExecuteChanged;
        public void Refresh() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }

    public sealed class AsyncCommand : ICommand
    {
        private readonly Func<Task> execute;
        private readonly Func<bool> canExecute;
        private readonly Action<Exception> onError;
        private bool running;
        public AsyncCommand(Func<Task> execute, Func<bool> canExecute, Action<Exception> onError)
        {
            this.execute = execute;
            this.canExecute = canExecute;
            this.onError = onError;
        }
        public bool CanExecute(object parameter) { return !running && canExecute(); }
        public async void Execute(object parameter) { await ExecuteAsync(); }
        public async Task ExecuteAsync()
        {
            if (!CanExecute(null)) return;
            running = true;
            Refresh();
            try { await execute(); }
            catch (Exception ex) { onError(ex); }
            finally { running = false; Refresh(); }
        }
        public event EventHandler CanExecuteChanged;
        public void Refresh() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
