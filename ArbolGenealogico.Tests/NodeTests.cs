using System;
using System.Collections.Generic;
using Xunit;
using ArbolGenealogico.Domain.Models;

namespace ArbolGenealogico.Tests
{
    public class NodeTests
    {
        // Helper: crear una Persona compatible con tu modelo
        private Persona CreatePersona(Guid? id = null, string? name = null)
        {
            return new Persona
            {
                id = id ?? Guid.NewGuid(),
                parentId = null,
                partnerId = null,
                name = name ?? "Persona",
                lon = 0.0,
                lat = 0.0,
                excludeFromDistance = false
            };
        }

        [Fact]
        public void Constructor_AssignsPersona()
        {
            var p = CreatePersona();
            var node = new Node(p);

            Assert.Equal(p, node.familiar);
        }

        [Fact]
        public void DetachFromParent_RemovesRelationship()
        {
            var parent = new Node(CreatePersona());
            var child = new Node(CreatePersona());

            parent.AddChild(child);
            child.DetachFromParent();

            Assert.Null(child.parent);
            Assert.Null(child.familiar.parentId);
            Assert.Empty(parent.children);
        }

        [Fact]
        public void AttachPartner_SetsBothPartnerIds()
        {
            var n1 = new Node(CreatePersona());
            var n2 = new Node(CreatePersona());

            n1.AttachPartner(n2);

            Assert.Equal(n2.familiar.id, n1.familiar.partnerId);
            Assert.Equal(n1.familiar.id, n2.familiar.partnerId);
        }

        [Fact]
        public void TransverseDFS_VisitsNodesInPreOrder()
        {
            var root = new Node(CreatePersona(name: "root"));
            var c1 = new Node(CreatePersona(name: "c1"));
            var c2 = new Node(CreatePersona(name: "c2"));
            var c1_1 = new Node(CreatePersona(name: "c1_1"));

            root.AddChild(c1);
            root.AddChild(c2);
            c1.AddChild(c1_1);

            var visited = new List<string>();
            root.TransverseDFS(n => visited.Add(n.familiar.name ?? n.familiar.id.ToString()));

            Assert.Equal(new List<string> { "root", "c1", "c1_1", "c2" }, visited);
        }
    }
}

