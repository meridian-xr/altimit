﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Random = System.Random;

namespace Altimit.UI
{
    public static partial class A
    {
        /*
        //Shortcut for adding action when panel shows
        public static Node OnShow(this Node go, object value)
        {
            go.AddOrGet<Panel>().onShowPanel += new Action<Node>(x => x.Set(value));
            return go;
        }
        */

        /*
        public static Node Update(this Node node)
        {
#if UNITY_5_3_OR_NEWER
            LayoutRebuilder.ForceRebuildLayoutImmediate(node.Get<RectTransform>());
            return node;
#elif GODOT
            return null;
#endif
        }
        */

        /*
        public static Node OnAwake(this Node go, Action<Node> onAwake)
        {
            return go.Hold<View>(x=> { x.onAwake += onAwake; });
        }

        //Shortcut for adding action when panel shows
        public static Node OnShow(this Node go, Action<Node> func)
        {
            go.AddOrGet<Panel>().onShow += func;
            return go;
        }*/

        //Dummy function
        public static Node Hold(this Node node)
        {
            return node;
        }
#if GODOT
        public static T Hold<T>(this T go) where T : Node
        {
            return go;
        }
#endif
        public static Node Hold(this Node go, Action<Node> func)
        {
            func(go);
            return go;
        }

        //Adds or gets a component
        public static Node Hold<T>(this Node node) where T : Node
        {
            T t;
            return node.Hold<T>(out t);
        }

        //Adds or gets a component
        public static Node Hold<T>(this Node node, out T t) where T : Node
        {
            if (node == null)
                throw new Exception("Attempted to hold a component using a Node that doesn't exist!");
            t = node.AddOrGet<T>();
            return node;
            //return (t=go.AddOrGet<T>()).Node;
        }

        //Adds or gets a component and sets values for it
        //Example: New<HorizontalLayoutGroup>(x => { x.padding = new RectOffset(0,0,0,0); });
        public static Node Hold<T>(this Node node, Action<T> func) where T : Node
        {
            T t;
            return node.Hold<T>(func, out t);
        }

        //Adds or gets a component and sets values for it
        //Example: New<HorizontalLayoutGroup>(x => { x.padding = new RectOffset(0,0,0,0); });
        public static Node Hold<T>(this Node go, Action<T> func, out T t) where T : Node
        {
            return go.Hold(false, func, out t);
        }


        public static Node Hold<T>(this Node node, bool includeChildren, Action<T> func) where T : Node
        {
            T t;
            return node.Hold<T>(includeChildren, func, out t);
        }

        public static Node Hold<T>(this Node node, bool includeChildren, out T t) where T : Node
        {
            t = node.AddOrGet<T>(includeChildren);
            return node;
        }

        public static Node Hold<T>(this Node node, bool includeChildren, Action<T> func, out T t) where T : Node
        {
            t = node.AddOrGet<T>(includeChildren);
            //Holder holder = go.AddOrGet<Holder>();
            //holder.OnHold += ()=> { func(t); };
            func(t);
            return node;
        }

        //Sets a Node's children
        public static Node Hold(this Node go, params Action<Node>[] children)
        {
            //children.ToList().ForEach(x => { x.transform.SetParent(go.transform); });
            return go;
        }

        public static Node HoldFirst(this Node node, params Node[] children)
        {
#if UNITY_5_3_OR_NEWER
            children.ToList().ForEach(x => { x.SetParent(node, true, true, true); x.transform.SetAsFirstSibling(); });
            return node;
#elif GODOT
            return null;
#endif
        }

        //Sets a Node's children
        public static T Hold<T>(this T node, params Node[] children) where T : Node
        {
            foreach (var child in children)
            {
                node.AddChild(child);
            }
            return node;

            /*
#if UNITY_5_3_OR_NEWER
            bool resetChildren = (node.GetComponent<RectTransform>() != null);
            children.ToList().ForEach(x => { x.transform.SetParent(node, resetChildren, resetChildren, resetChildren); });
            return node;
#elif GODOT
            foreach (var child in children)
            {
                node.AddChild(child);
            }
            return node;
#endif
            */
        }

        //Sets a Node's children
        public static Node Switch(this Node go, Node target)
        {
            go.Release();
            go.Hold(target);
            return go;
        }

        //Sets a Node's children
        public static Node Release(this Node node)
        {
#if UNITY_5_3_OR_NEWER
            node.transform.DetachChildren();
            return node;
#elif GODOT
            return null;
#endif
        }

        //Returns an empty Node
        public static Node New(string name = "")
        {
            Node node = new Node(name == "" ? OS.Random.Next(0, 1000).ToString() : name);
            return node;
        }

        // TODO: Call generic version instead, or have generic refer to this
        public static Node AddOrGet(this Node node, Type type, bool includeChildren = false)
        {
#if UNITY_5_3_OR_NEWER
            var comp = node.Get(type, includeChildren);
            if (comp == null)
                return node.gameObject.AddComponent(type);
            return comp;
#elif GODOT
            return null;
#endif
        }

        //Adds or gets a component
        public static T AddOrGet<T>(this Node go, bool includeChildren = false) where T : Node
        {
            return (T)go.AddOrGet(typeof(T), includeChildren);
        }

        //Returns if a Node has a component or not
        public static bool Has<T>(this Node go, bool includeChildren = false) where T : Node
        {
            return go.Get<T>(includeChildren) != null;
        }

        //Gets a component
        public static T GetInParent<T>(this Node go) where T : Node
        {
#if UNITY_5_3_OR_NEWER
            return (go == null ? null : go.GetComponentInParent<T>());
#elif GODOT
            return null;
#endif
        }

        //Gets a component
        public static T[] GetInParents<T>(this Node node) where T : Node
        {
#if UNITY_5_3_OR_NEWER
            return (node == null ? new T[0] : node.GetComponentsInParent<T>());
#elif GODOT
            return null;
#endif
        }

        //Gets a component
        public static T Get<T>(this Node node, bool includeChildren = false) where T : Node
        {
            return (T)node.Get(typeof(T), includeChildren);
        }

        public static Node Get(this Node node, Type type, bool includeChildren = false)
        {
#if UNITY_5_3_OR_NEWER
            if (node == null)
                throw new Exception("Tried setting component on a null Node!");
            if (includeChildren)
                return node.GetComponentInChildren(type);

            return node.GetComponent(type);
#elif GODOT
            return null;
#endif
        }

        //Gets a component
        public static Node Get(this Node go)
        {
            if (go == null)
                go = New();
            return go;
        }

        //Sets a Node's children
        public static Node Get<T>(this Node node, Action<T> func) where T : Node
        {
            if (node.Has<T>())
            {
                func(node.Get<T>());
            }
            return node;
        }

        public static Node OnHeld(this Node node, Action<Node> func)
        {
            /*
#if UNITY_5_3_OR_NEWER
            node.Hold<ParentObserver>(x =>
            {
                x.onUpdateSingle += func;
            });
#elif GODOT
#endif
            */
            return node;
        }

        public static Node OnNextFrame(this Node node, Action<Node> func)
        {
            ExecuteFunc(node, func);
            return node;
        }

        static async void ExecuteFunc(Node node, Action<Node> func)
        {
            await Task.Delay(2000);
            func(node);
        }

        /*
        public static Node SetParent(this Node node, Node parent, bool setPosition = false, bool setRotation = false, bool setScale = false)
        {
#if UNITY_5_3_OR_NEWER
            node.transform.SetParent(parent.transform);
            if (setPosition)
                node.transform.localPosition = Vector3.zero;
            if (setRotation)
                node.transform.localEulerAngles = Vector3.zero;
            if (setScale)
                node.transform.localScale = Vector3.one;
#elif GODOT
#endif
            return node;
        }
        */

        /*
        public static RectTransform rectTransform(this Node go)
        {
            return go.GetComponent<RectTransform>();
        }
        */

        public static Node Call(this Node go, Action<Node> action)
        {
            if (action != null)
                action(go);
            return go;
        }
    }
}