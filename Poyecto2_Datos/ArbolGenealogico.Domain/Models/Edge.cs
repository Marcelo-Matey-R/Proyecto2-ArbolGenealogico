using System.Globalization;
using ArbolGenealogico.Domain.Models;

namespace ArbolGenealogico.Domain.Models
{
    public class Edge
    {
        #region Atributos
        public Node fam1 { get; }
        public Node fam2 { get; }

        public double weight { get; }
        #endregion

        #region Constructor
        public Edge(Node per1, Node per2, double w)
        {
            this.fam1 = per1 ?? throw new ArgumentNullException(nameof(per1));
            this.fam2 = per2 ?? throw new ArgumentNullException(nameof(per2));

            this.weight = w;
        }
        #endregion

        #region Helpers
        public bool IsValid() => !double.IsNaN(weight) && !double.IsInfinity(weight);
        public override string ToString() => $"{fam1.familiar.name} -> {fam2.familiar.name}: {weight:F6}";
        #endregion
    }
}
