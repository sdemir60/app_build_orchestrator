namespace Mini
{
    public class MyControl : System.Windows.Controls.Control
    {
        public string Title { get; set; }
        internal string Hidden { get; set; }
        public int Compute() { return Title == null ? 0 : Title.Length + 41; }
    }

    internal class InternalControl : System.Windows.Controls.Control
    {
        public string Title { get; set; }
    }
}
