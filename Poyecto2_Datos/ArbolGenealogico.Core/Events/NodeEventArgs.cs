using Systmen.Globalization;

namespace ArbolGenealogico.Core.Events
{
  public class NodeEventArgs : EventArgs
  {
        public Node Node { get; }
        public NodeEventArgs(Node node) { Node = node; }
  }
}
