using System.Globalization;
using ArbolGenealogico.Domain.Models;

namespace ArbolGenealogico.Domain.Models
{
    public class Node
    {
        public Persona familiar { get; }

        private Node? _parent;
        public Node? parent => _parent;
        private readonly List<Node> _children = new List<Node>();
        public IReadOnlyList<Node> children => _children.AsReadOnly();

        public Dictionary<Node, double> distances = new Dictionary<Node, double>();
        public List<Edge> edges = new List<Edge>();

        public Node(Persona fam, string part = "")
        {
            this.familiar = fam ?? throw new ArgumentNullException(nameof(fam));
        }

        public void AddChild(Node child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));

            if (!_children.Contains(child))
            {
                child.DetachFromParent();
                child._parent = this;
                _children.Add(child);
                child.familiar.parentId = this.familiar.id;
            }
        }
        public void DetachFromParent()
        {
            if (_parent != null)
            {
                _parent._children.Remove(this);
                _parent = null;
                this.familiar.parentId = null;
            }
        }
            public void DetachFromPartner(Node? partnerNode = null)
        {
            // Limpiar el partnerId local
            if (familiar.partnerId.HasValue)
            {
                // si se pasó partnerNode y coincide, limpiar partnerNode también
                if (partnerNode != null && partnerNode.familiar.partnerId.HasValue &&
                    partnerNode.familiar.partnerId.Value == this.familiar.id &&
                    this.familiar.partnerId.Value == partnerNode.familiar.id)
                {
                    // limpiar ambos
                    partnerNode.familiar.partnerId = null;
                    this.familiar.partnerId = null;
                    return;
                }

                // si no se pasó partnerNode, o no coincide, sólo limpiar local
                this.familiar.partnerId = null;
            }
            else
            {
                // si no había partner local y se pasó partnerNode, asegurarse de limpiar el otro lado si apunta aquí
                if (partnerNode != null && partnerNode.familiar.partnerId.HasValue &&
                    partnerNode.familiar.partnerId.Value == this.familiar.id)
                {
                    partnerNode.familiar.partnerId = null;
                }
            }
        }
        public void AttachPartner(Node partnerNode)
        {
            if (partnerNode == null) throw new ArgumentNullException(nameof(partnerNode));
            this.familiar.partnerId = partnerNode.familiar.id;
            partnerNode.familiar.partnerId = this.familiar.id;
        }
        public int GetLevel()
        {
            int lvl = 0;
            var curr = this._parent;

            while (curr != null)
            {
                lvl++;
                curr = curr._parent;
            }
            return lvl;
        }

        public void TransverseDFS(Action<Node> action)
        {
            action?.Invoke(this);
            foreach (var c in children)
            {
                c.TransverseDFS(action);
            }
        }

    }
}