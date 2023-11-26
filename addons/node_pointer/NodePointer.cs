using Godot;
using System;
using System.Linq;

namespace Ardot.Nodes.NodePointer;

///<summary>
///Gives the ability to move nodes around a scene at runtime without breaking node path references.<para/>
///This is done by leaving <c>NodePointer</c> nodes that pretend to be the old node at their original position.<para/>
///Nodes that are reparented back to their original parent will have no <c>NodePointer</c>. This also applies to any child of any reparented node.<para/>
///For this to function correctly, always use <c>NodePointer.ReparentNode()</c> to move nodes that you want to keep referrable, and use <c>NodePointer.GetNode()</c>
///or <c>NodePointer.GetNodeOrNull()</c> to get nodes that have been reparented.<para/>
///When reparenting nodes, you can make <c>NodePointer</c> ignore them and not initialize a <c>NodePointer</c> node by adding the metadata "node_pointer_ignore" or "NPI". 
///Consequently, this will also ignore all children of an ignored node. To ignore only children, and not a parent node, add the metadata "node_pointer_ignore_children" or "NPIC".
///Alternatively, to avoid the use of metadata, or for more specific use cases, <c>NodePointer.ReparentNode()</c> has the option to ignore certain nodes via a predicate match.<para/>
///<b>Note:</b> Any node that is reparented (any number of times) will always have at most one <c>NodePointer</c>, at its original position.<para/>
///<b>Note:</b> To be able to search backwards from a reparented node, the metadata "NP" is added to any reparented node. 
///This metadata stores a reference to the node's NodePointer and is deleted if the node is reparented back to its original location.<para/>
///<b>Warning:</b> Never add non-<c>NodePointer</c> children to any <c>NodePointer</c>. This will break stuff in a lot of ways.<para/>
///<b>Functions:</b><br/>
///- <c>ReparentNode(Node node, Node newParent, bool keepGlobalTransform, Predicate&lt;Node&gt; ignoreChildMatch) *static</c><br/>
///- <c>GetChildren(Node parent, bool getMovedNodes) *static</c><br/>
///- <c>GetNodePath(Node node) *static</c><br/>
///- <c>GetNode&lt;T&gt;(Node parent, NodePath nodePath, bool getMovedNodes) *static</c><br/>
///- <c>GetNodeOrNull&lt;T&gt;(Node parent, NodePath nodePath, bool getMovedNodes = false) *static</c><br/>
///</summary>
public partial class NodePointer : Node
{
	//Names used for various metadata (can be modified)
	private static readonly StringName 
	NodePointerMeta = "NP",
	NodePointerIgnoreMetaAlt = "NPI",
	NodePointerIgnoreChildrenMetaAlt = "NPIC",
	NodePointerIgnoreMeta = "node_pointer_ignore",
	NodePointerIgnoreChildrenMeta = "node_pointer_ignore_children";

	private Node node;

	///<summary>
	///Reparents a node, and keeps a <c>NodePointer</c> at the node's original position so as to preserve any node paths that lead to the node.
	///Otherwise, this is functionally the same as <c>Node.Reparent()</c>.<para/>
	///Use <c>NodePointer.GetNode()</c> or <c>NodePointer.GetNodeOrNull()</c> to get nodes that have been reparented with this method.<para/>
	///<b>Note:</b><br/>
	///In case you want to ignore nodes that won't be accessed later, and don't want to use metadata, you can use the <c>ignoreChildMatch</c> parameter.
	///</summary>
	///<param name = 'node'>The node to reparent</param>
	///<param name = 'newParent'>The new parent of node</param>
	///<param name = 'keepGlobalTransform'>Whether to keep the global transform of node the same.</param>
	///<param name = 'ignoreChildMatch'>Runs for every child node of the node being reparented to check whether they should be ignored by NodePointer. (functions the same as using the metadata "NPI" or "node_pointer_ignore" if returns true)</param>
	public static void ReparentNode(Node node, Node newParent, bool keepGlobalTransform = false, Predicate<Node> ignoreChildMatch = null)
	{
		Node oldParent = node.GetParent();

		if(oldParent == newParent || node.HasMeta(NodePointerIgnoreMeta) || node.HasMeta(NodePointerIgnoreMetaAlt))
			return;

		Variant globalTransform = node.Get(Node2D.PropertyName.GlobalTransform);

		oldParent.RemoveChild(node);

		if (!node.HasMeta(NodePointerMeta))
        {
            NodePointer nodePointer = InitNodePointer(node, oldParent);

            if (!node.HasMeta(NodePointerIgnoreChildrenMeta) && !node.HasMeta(NodePointerIgnoreChildrenMetaAlt) && (ignoreChildMatch == null || !ignoreChildMatch.Invoke(node)))
                foreach (Node child in node.GetChildren())
                    InitChildPointers(child, nodePointer);
        }
        else if(node.GetMeta(NodePointerMeta).AsGodotObject() is NodePointer nodePointer && newParent == nodePointer.GetParent())
		{   
			MergePointerTree(nodePointer);
			newParent.RemoveChild(nodePointer);
			nodePointer.QueueFree();
		}

		newParent.AddChild(node);

		if(keepGlobalTransform)
			node.Set(Node2D.PropertyName.GlobalTransform, globalTransform);
	}

	///<summary>Gets all children of parent, Functions the same as <c>Node.GetChildren()</c>, but nodes that are referenced by <c>NodePointer</c>s will be returned directly.<para/>
	///<b>Notes:</b> <br/>
	///- Nodes that have been moved from their original position in the tree will not be returned, this is to prevent issues
	///when searching through a tree. If you really want to get nodes directly, set <c>getMovedNodes</c> to true.
	///</summary>
	///<param name = 'parent'>The node to get children from.</param>
	///<param name = 'getMovedNodes'>Whether to return nodes that have been moved from their original position in the tree.</param>
	public static Godot.Collections.Array<Node> GetChildren(Node parent, bool getMovedNodes = false)
	{
		Godot.Collections.Array<Node> children = parent.GetChildren();

		if(getMovedNodes)
			return children;

		for(int x = 0; x < children.Count; x++)
		{
			Node child = children[x];

			if(child.HasMeta(NodePointerMeta))
			{
				children.RemoveAt(x);
				x--;
			}
			else if(child is NodePointer pointer)
				children[x] = pointer.node;
		}

		return children;
	}	

	///<summary>Returns the absolute path of node, functions the same as <c>Node.GetPath()</c>, but nodes that are referenced by <c>NodePointers</c> will return the <c>NodePointer</c>'s path.</summary>
	///<param name = 'node'>The node to get the path of.</param>
	public static NodePath GetNodePath(Node node)
	{
		if(node.HasMeta(NodePointerMeta) && node.GetMeta(NodePointerMeta).AsGodotObject() is NodePointer nodePointer)
			return nodePointer.GetPath();
		
		return node.GetPath();
	}

	///<summary>
	///Fetches a node relative to parent, functions the same as <c>Node.GetNode()</c>, but nodes that are referenced by <c>NodePointers</c> will be returned directly.<para/>
	///<b>Note:</b> Any node will always get nodes relative its original position in the tree with this function. <para/>
	///<b>Note:</b> Nodes that have been moved from their original position in the tree will not be returned if <c>nodePath</c> directly leads to them, this is to prevent issues<br/>
	///when searching through a tree. If you really want to get nodes directly, set <c>getMovedNodes</c> to true.
	///</summary>
	///<summary></summary>
	///<param name = 'parent'>The node that <c>Node.GetNode()</c> will be called from to fetch nodes.</param>
	///<param name = 'nodePath'>The path that the node will be fetched from.</param>
	///<param name = 'getMovedNodes'>Whether to return nodes that have been moved from their original position in the tree.</param>
	public static T GetNode<T>(Node parent, NodePath nodePath, bool getMovedNodes = false) where T : Node
	{
		return GetNode(parent, nodePath, getMovedNodes) as T;
	}

	///<summary>
	///Fetches a node relative to parent, functions the same as <c>Node.GetNode()</c>, but nodes that are referenced by <c>NodePointers</c> will be returned directly.<para/>
	///<b>Note:</b> Any node will always get nodes relative its original position in the tree with this function. <para/>
	///<b>Note:</b> Nodes that have been moved from their original position in the tree will not be returned if <c>nodePath</c> directly leads to them, this is to prevent issues<br/>
	///when searching through a tree. If you really want to get nodes directly, set <c>getMovedNodes</c> to true.
	///</summary>
	///<summary></summary>
	///<param name = 'parent'>The node that <c>Node.GetNode()</c> will be called from to fetch nodes.</param>
	///<param name = 'nodePath'>The path that the node will be fetched from.</param>
	///<param name = 'getMovedNodes'>Whether to return nodes that have been moved from their original position in the tree.</param>
	public static Node GetNode(Node parent, NodePath nodePath, bool getMovedNodes = false)
	{
		Node virtualParent = parent;

		if(parent == null)
			throw new NullReferenceException("The field 'parent' of NodePointer.GetNode cannot be null");    

		if(parent.HasMeta(NodePointerMeta) && parent.GetMeta(NodePointerMeta).AsGodotObject() is NodePointer nodePointer)
			virtualParent = nodePointer;

		Node foundNode = virtualParent.GetNode(nodePath);

		if(!getMovedNodes && foundNode.HasMeta(NodePointerMeta))
			throw new NodePointerException("Cannot get a node that is referenced by a NodePointer directly, either use its original position in the tree or set 'getMovedNodes' to true.");   

		if(foundNode is NodePointer foundNodePointer)  
			return foundNodePointer.node;

		return foundNode;
	}

	///<summary>
	///Fetches a node relative to parent, functions the same as <c>Node.GetNodeOrNull()</c>, but nodes that are referenced by <c>NodePointers</c> will be returned directly.<para/>
	///<b>Note:</b> Any node will always get nodes relative its original position in the tree with this function. <para/>
	///<b>Note:</b> Nodes that have been moved from their original position in the tree will not be returned if <c>nodePath</c> directly leads to them, this is to prevent issues<br/>
	///when searching through a tree. If you really want to get nodes directly, set <c>getMovedNodes</c> to true.
	///</summary>
	///<summary></summary>
	///<param name = 'parent'>The node that <c>Node.GetNodeOrNull()</c> will be called from to fetch nodes.</param>
	///<param name = 'nodePath'>The path that the node will be fetched from.</param>
	///<param name = 'getMovedNodes'>Whether to return nodes that have been moved from their original position in the tree.</param>
	public static T GetNodeOrNull<T>(Node parent, NodePath nodePath, bool getMovedNodes = false) where T : Node
	{
		return GetNodeOrNull(parent, nodePath, getMovedNodes) as T;
	}

	///<summary>
	///Fetches a node relative to parent, functions the same as <c>Node.GetNodeOrNull()</c>, but nodes that are referenced by <c>NodePointers</c> will be returned directly.<para/>
	///<b>Note:</b> Any node will always get nodes relative its original position in the tree with this function. <para/>
	///<b>Note:</b> Nodes that have been moved from their original position in the tree will not be returned if <c>nodePath</c> directly leads to them, this is to prevent issues<br/>
	///when searching through a tree. If you really want to get nodes directly, set <c>getMovedNodes</c> to true.
	///</summary>
	///<summary></summary>
	///<param name = 'parent'>The node that <c>Node.GetNodeOrNull()</c> will be called from to fetch nodes.</param>
	///<param name = 'nodePath'>The path that the node will be fetched from.</param>
	///<param name = 'getMovedNodes'>Whether to return nodes that have been moved from their original position in the tree.</param>
	public static Node GetNodeOrNull(Node parent, NodePath nodePath, bool getMovedNodes = false)
	{
		Node virtualParent = parent;

		if(parent == null)
			throw new NullReferenceException("The field 'parent' of NodePointer.GetNodeOrNull cannot be null");    

		if(parent.HasMeta(NodePointerMeta) && parent.GetMeta(NodePointerMeta).AsGodotObject() is NodePointer nodePointer)
			virtualParent = nodePointer;

		Node foundNode = virtualParent.GetNodeOrNull(nodePath);
		
		if(!getMovedNodes && foundNode.HasMeta(NodePointerMeta))
			return null; 

		if(foundNode is NodePointer foundNodePointer)  
			return foundNodePointer.node;

		return foundNode;
	}

	private static void InitChildPointers(Node childNode, NodePointer parentPointer)
	{
		if(childNode is NodePointer)
		{
			childNode.GetParent().RemoveChild(childNode);
			parentPointer.AddChild(childNode);
		}
		else if (!childNode.HasMeta(NodePointerMeta))
		{
			NodePointer pointer = InitNodePointer(childNode, parentPointer);

			foreach (Node child in childNode.GetChildren())
				InitChildPointers(child, pointer);
		}
	}

	private static void MergePointerTree(NodePointer parentPointer)
	{
		foreach (NodePointer childPointer in parentPointer.GetChildren().Cast<NodePointer>())
		{
			if(parentPointer.node.GetNodeOrNull(childPointer.Name.ToString()) == null)
			{
				parentPointer.RemoveChild(childPointer);
				parentPointer.node.AddChild(childPointer);
				continue;
			}

			MergePointerTree(childPointer);
		}

		parentPointer.node.RemoveMeta(NodePointerMeta);
	}

	private static NodePointer InitNodePointer(Node node, Node parent)
	{
		NodePointer nodePointer = new()
		{
			Name = node.Name,
			node = node,
		};

		node.SetMeta(NodePointerMeta, nodePointer);
		parent.AddChild(nodePointer);

		return nodePointer;
	}

	private class NodePointerException : Exception
	{
		public NodePointerException(string message)
		{
			_message = message;
		}

		private readonly string _message;
		public override string Message => _message;
	}
}
