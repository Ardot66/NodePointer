# NodePointer (C#)
NodePointer is a system designed to make it easy to keep references to nodes in complex and changing scene trees, and is mainly intended to keep NodePath references valid at runtime, even when nodes have been reparented.

**Features:**
- Introduces a new node, NodePointer, which will keep a reference to a reparented node at the node's original position in the tree.
- Includes a variety of functions that allow for easy integration of NodePointers into any system.

For more information, see the in-code documentation.

# Example

You have a node, NPC, that you want to instantiate into scenes; and you have another node, Player.
Any NPC needs to be able to find a reference the player so it can function. While you could create a global class 
