﻿/*
* Box2D.XNA port of Box2D:
* Copyright (c) 2009 Brandon Furtwangler, Nathan Furtwangler
*
* Original source Box2D:
* Copyright (c) 2006-2009 Erin Catto http://www.gphysics.com 
* 
* This software is provided 'as-is', without any express or implied 
* warranty.  In no event will the authors be held liable for any damages 
* arising from the use of this software. 
* Permission is granted to anyone to use this software for any purpose, 
* including commercial applications, and to alter it and redistribute it 
* freely, subject to the following restrictions: 
* 1. The origin of this software must not be misrepresented; you must not 
* claim that you wrote the original software. If you use this software 
* in a product, an acknowledgment in the product documentation would be 
* appreciated but is not required. 
* 2. Altered source versions must be plainly marked as such, and must not be 
* misrepresented as being the original software. 
* 3. This notice may not be removed or altered from any source distribution. 
*/

using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Box2D.XNA
{
    [Flags]
    public enum WorldFlags
    {
        NewFixture  = (1 << 0),
        Locked      = (1 << 1),
        ClearForces = (1 << 2),
    };

    /// The world class manages all physics entities, dynamic simulation,
    /// and asynchronous queries. The world also contains efficient memory
    /// management facilities.
    public class World
    {
        /// ruct a world object.
        /// @param gravity the world gravity vector.
        /// @param doSleep improve performance by not simulating inactive bodies.
        public World(Vector2 gravity, bool doSleep)
        {
            WarmStarting = true;
            ContinuousPhysics = true;

            _allowSleep = doSleep;
            Gravity = gravity;

            _flags = WorldFlags.ClearForces;

            _queryAABBCallbackWrapper = QueryAABBCallbackWrapper;
            _rayCastCallbackWrapper = RayCastCallbackWrapper;
        }

	    /// Register a destruction listener.
	    public IDestructionListener DestructionListener { get; set; }

	    /// Register a contact filter to provide specific control over collision.
	    /// Otherwise the default filter is used (Settings.b2_defaultFilter).
	    public IContactFilter ContactFilter
        {
            get
            {
                return _contactManager.ContactFilter;
            }
            set
            {
                _contactManager.ContactFilter = value;
            }
        }

	    /// Register a contact event listener
	    public IContactListener ContactListener
        {
            get
            {
                return _contactManager.ContactListener;
            }
            set
            {
                _contactManager.ContactListener = value;
            }
        }

	    /// Register a routine for debug drawing. The debug draw functions are called
	    /// inside the World.Step method, so make sure your renderer is ready to
	    /// consume draw commands when you call Step().
	    public DebugDraw DebugDraw { get; set; }

	    /// Create a rigid body given a definition. No reference to the definition
	    /// is retained.
	    /// @warning This function is locked during callbacks.
	    public Body CreateBody(BodyDef def)
        {
            Debug.Assert(!IsLocked);
	        if (IsLocked)
	        {
		        return null;
	        }

	        var b = new Body(def, this);

	        // Add to world doubly linked list.
	        b._prev = null;
	        b._next = _bodyList;
	        if (_bodyList != null)
	        {
		        _bodyList._prev = b;
	        }
	        _bodyList = b;
	        ++_bodyCount;

	        return b;
        }

	    /// Destroy a rigid body given a definition. No reference to the definition
	    /// is retained. This function is locked during callbacks.
	    /// @warning This automatically deletes all associated shapes and joints.
	    /// @warning This function is locked during callbacks.
	    public void DestroyBody(Body b)
        {
            Debug.Assert(_bodyCount > 0);
	        Debug.Assert(!IsLocked);
	        if (IsLocked)
	        {
		        return;
	        }

	        // Delete the attached joints.
	        JointEdge je = b._jointList;
	        while (je != null)
	        {
		        JointEdge je0 = je;
		        je = je.Next;

		        if (DestructionListener != null)
		        {
			        DestructionListener.SayGoodbye(je0.Joint);
		        }

		        DestroyJoint(je0.Joint);
	        }
	        b._jointList = null;

	        // Delete the attached contacts.
	        ContactEdge ce = b._contactList;
	        while (ce != null)
	        {
		        ContactEdge ce0 = ce;
		        ce = ce.Next;
		        _contactManager.Destroy(ce0.Contact);
	        }
	        b._contactList = null;

	        // Delete the attached fixtures. This destroys broad-phase proxies.
	        Fixture f = b._fixtureList;
	        while (f != null)
	        {
		        Fixture f0 = f;
		        f = f._next;

		        if (DestructionListener != null)
		        {
			        DestructionListener.SayGoodbye(f0);
		        }

                f0.DestroyProxies(_contactManager._broadPhase);
		        f0.Destroy();
	        }
	        b._fixtureList = null;
	        b._fixtureCount = 0;

	        // Remove world body list.
	        if (b._prev != null)
	        {
		        b._prev._next = b._next;
	        }

            if (b._next != null)
	        {
		        b._next._prev = b._prev;
	        }

	        if (b == _bodyList)
	        {
		        _bodyList = b._next;
	        }

	        --_bodyCount;
        }

	    /// Create a joint to rain bodies together. No reference to the definition
	    /// is retained. This may cause the connected bodies to cease colliding.
	    /// @warning This function is locked during callbacks.
	    public Joint CreateJoint(JointDef def)
        {
	        Debug.Assert(!IsLocked);
	        if (IsLocked)
	        {
		        return null;
	        }

	        Joint j = Joint.Create(def);

	        // Connect to the world list.
	        j._prev = null;
	        j._next = _jointList;
	        if (_jointList != null)
	        {
		        _jointList._prev = j;
	        }
	        _jointList = j;
	        ++_jointCount;

	        // Connect to the bodies' doubly linked lists.
	        j._edgeA.Joint = j;
	        j._edgeA.Other = j._bodyB;
	        j._edgeA.Prev = null;
	        j._edgeA.Next = j._bodyA._jointList;

	        if (j._bodyA._jointList != null) 
                j._bodyA._jointList.Prev = j._edgeA;

	        j._bodyA._jointList = j._edgeA;

	        j._edgeB.Joint = j;
	        j._edgeB.Other = j._bodyA;
	        j._edgeB.Prev = null;
	        j._edgeB.Next = j._bodyB._jointList;

	        if (j._bodyB._jointList != null) 
                j._bodyB._jointList.Prev = j._edgeB;

	        j._bodyB._jointList = j._edgeB;

	        Body bodyA = def.bodyA;
	        Body bodyB = def.bodyB;

	        bool staticA = bodyA.GetType() == BodyType.Static;
	        bool staticB = bodyB.GetType() == BodyType.Static;

	        // If the joint prevents collisions, then flag any contacts for filtering.
	        if (def.collideConnected == false)
	        {
		        ContactEdge edge = bodyB.GetContactList();
		        while (edge != null)
		        {
			        if (edge.Other == bodyA)
			        {
				        // Flag the contact for filtering at the next time step (where either
				        // body is awake).
				        edge.Contact.FlagForFiltering();
			        }

			        edge = edge.Next;
		        }
	        }

	        // Note: creating a joint doesn't wake the bodies.

	        return j;
        }

	    /// Destroy a joint. This may cause the connected bodies to begin colliding.
	    /// @warning This function is locked during callbacks.
	    public void DestroyJoint(Joint j)
        {
	        Debug.Assert(!IsLocked);
	        if (IsLocked)
	        {
		        return;
	        }

	        bool collideConnected = j._collideConnected;

	        // Remove from the doubly linked list.
	        if (j._prev != null)
	        {
		        j._prev._next = j._next;
	        }

	        if (j._next != null)
	        {
		        j._next._prev = j._prev;
	        }

	        if (j == _jointList)
	        {
		        _jointList = j._next;
	        }

	        // Disconnect from island graph.
	        Body bodyA = j._bodyA;
	        Body bodyB = j._bodyB;

	        // Wake up connected bodies.
	        bodyA.SetAwake(true);
	        bodyB.SetAwake(true);

	        // Remove from body 1.
	        if (j._edgeA.Prev != null)
	        {
		        j._edgeA.Prev.Next = j._edgeA.Next;
	        }

	        if (j._edgeA.Next != null)
	        {
		        j._edgeA.Next.Prev = j._edgeA.Prev;
	        }

	        if (j._edgeA == bodyA._jointList)
	        {
		        bodyA._jointList = j._edgeA.Next;
	        }

	        j._edgeA.Prev = null;
	        j._edgeA.Next = null;

	        // Remove from body 2
	        if (j._edgeB.Prev != null)
	        {
		        j._edgeB.Prev.Next = j._edgeB.Next;
	        }

	        if (j._edgeB.Next != null)
	        {
		        j._edgeB.Next.Prev = j._edgeB.Prev;
	        }

	        if (j._edgeB == bodyB._jointList)
	        {
		        bodyB._jointList = j._edgeB.Next;
	        }

	        j._edgeB.Prev = null;
	        j._edgeB.Next = null;

	        Debug.Assert(_jointCount > 0);
	        --_jointCount;

	        // If the joint prevents collisions, then flag any contacts for filtering.
	        if (collideConnected == false)
	        {
		        ContactEdge edge = bodyB.GetContactList();
		        while (edge != null)
		        {
			        if (edge.Other == bodyA)
			        {
				        // Flag the contact for filtering at the next time step (where either
				        // body is awake).
				        edge.Contact.FlagForFiltering();
			        }

			        edge = edge.Next;
		        }
	        }
        }

	    /// Take a time step. This performs collision detection, integration,
	    /// and raint solution.
	    /// @param timeStep the amount of time to simulate, this should not vary.
	    /// @param velocityIterations for the velocity raint solver.
	    /// @param positionIterations for the position raint solver.
	    public void Step(float dt, int velocityIterations, int positionIterations)
        {
	        // If new fixtures were added, we need to find the new contacts.
	        if ((_flags & WorldFlags.NewFixture) == WorldFlags.NewFixture)
	        {
		        _contactManager.FindNewContacts();
		        _flags &= ~WorldFlags.NewFixture;
	        }

	        _flags |= WorldFlags.Locked;

	        TimeStep step;
	        step.dt = dt;
	        step.velocityIterations	= velocityIterations;
	        step.positionIterations = positionIterations;
	        if (dt > 0.0f)
	        {
		        step.inv_dt = 1.0f / dt;
	        }
	        else
	        {
		        step.inv_dt = 0.0f;
	        }

	        step.dtRatio = _inv_dt0 * dt;

	        step.warmStarting = WarmStarting;

	        // Update contacts. This is where some contacts are destroyed.
	        _contactManager.Collide();

	        // Integrate velocities, solve velocity raints, and integrate positions.
	        if (step.dt > 0.0f)
	        {
		        Solve(ref step);
	        }

	        // Handle TOI events.
	        if (ContinuousPhysics && step.dt > 0.0f)
	        {
		        SolveTOI();
	        }

	        if (step.dt > 0.0f)
	        {
		        _inv_dt0 = step.inv_dt;
	        }

            if ((_flags & WorldFlags.ClearForces) != 0)
            {
                ClearForces();
            }

	        _flags &= ~WorldFlags.Locked;
        }

        /// Call this after you are done with time steps to clear the forces. You normally
	    /// call this after each call to Step, unless you are performing sub-steps. By default,
	    /// forces will be automatically cleared, so you don't need to call this function.
	    /// @see SetAutoClearForces
        public void ClearForces()
        {
            for (Body body = _bodyList; body != null; body = body.GetNext())
            {
                body._force = Vector2.Zero;
                body._torque = 0.0f;
            }
        }

        /// Set flag to control automatic clearing of forces after each time step.
	    void SetAutoClearForces(bool flag)
        {
            if (flag)
            {
                _flags |= WorldFlags.ClearForces;
            }
            else
            {
                _flags &= ~WorldFlags.ClearForces;
            }
        }

	    /// Get the flag that controls automatic clearing of forces after each time step.
        bool GetAutoClearForces()
        {
            return (_flags & WorldFlags.ClearForces) == WorldFlags.ClearForces;
        }

        /// Get the contact manager for testing.
        public ContactManager GetContactManager()
        {
            return _contactManager;
        }

	    /// Call this to draw shapes and other debug draw data.
	    public void DrawDebugData()
        {
	        if (DebugDraw == null)
	        {
		        return;
	        }

	        DebugDrawFlags flags = DebugDraw.Flags;

	        if ((flags & DebugDrawFlags.Shape) == DebugDrawFlags.Shape)
	        {
		        for (Body b = _bodyList; b != null; b = b.GetNext())
		        {
                    Transform xf;
                    b.GetTransform(out xf);
			        for (Fixture f = b.GetFixtureList(); f != null; f = f.GetNext())
			        {
                        if (b.IsActive() == false)
                        {
                            DrawShape(f, xf, new Color(0.5f, 0.5f, 0.3f));
                        }
                        else if (b.GetType() == BodyType.Static)
                        {
                            DrawShape(f, xf, new Color(0.5f, 0.9f, 0.5f));
                        }
                        else if (b.GetType() == BodyType.Kinematic)
                        {
                            DrawShape(f, xf, new Color(0.5f, 0.5f, 0.9f));
                        }
				        else if (b.IsAwake() == false)
				        {
					        DrawShape(f, xf, new Color(0.6f, 0.6f, 0.6f));
				        }
				        else
				        {
					        DrawShape(f, xf, new Color(0.9f, 0.7f, 0.7f));
				        }
			        }
		        }
	        }

	        if ((flags & DebugDrawFlags.Joint) == DebugDrawFlags.Joint)
	        {
		        for (Joint j = _jointList; j != null; j = j.GetNext())
		        {
				    DrawJoint(j);
		        }
	        }

	        if ((flags & DebugDrawFlags.Pair) == DebugDrawFlags.Pair)
	        {
		        Color color = new Color(0.3f, 0.9f, 0.9f);
		        for (Contact c = _contactManager._contactList; c != null; c = c.GetNext())
		        {
			      /*  
                   Fixture fixtureA = c.GetFixtureA();
			       Fixture fixtureB = c.GetFixtureB();

                   AABB aabbA;
                   AABB aabbB;
                   fixtureA.GetAABB(out aabbA);
                   fixtureB.GetAABB(out aabbB);

                   Vector2 cA = aabbA.GetCenter();
                   Vector2 cB = aabbB.GetCenter();

                   DebugDraw.DrawSegment(cA, cB, color);
                  */
		        }
	        }

	        if ((flags & DebugDrawFlags.AABB) == DebugDrawFlags.AABB)
	        {
		        Color color = new Color(0.9f, 0.3f, 0.9f);
		        BroadPhase bp = _contactManager._broadPhase;

		        for (Body b = _bodyList; b != null; b = b.GetNext())
		        {
                    if (b.IsActive() == false)
			        {
				        continue;
			        }

			        for (Fixture f = b.GetFixtureList(); f != null; f = f.GetNext())
			        {
                        for (int i = 0; i < f._proxyCount; ++i)
				        {
					        FixtureProxy proxy = f._proxies[i];
					        AABB aabb;
                            bp.GetFatAABB(proxy.proxyId, out aabb);
                            FixedArray8<Vector2> vs = new FixedArray8<Vector2>();
					        vs[0] = new Vector2(aabb.lowerBound.X, aabb.lowerBound.Y);
					        vs[1] = new Vector2(aabb.upperBound.X, aabb.lowerBound.Y);
					        vs[2] = new Vector2(aabb.upperBound.X, aabb.upperBound.Y);
					        vs[3] = new Vector2(aabb.lowerBound.X, aabb.upperBound.Y);

					        DebugDraw.DrawPolygon(ref vs, 4, color);
				        }
			        }
		        }
	        }

	        if ((flags & DebugDrawFlags.CenterOfMass) == DebugDrawFlags.CenterOfMass)
	        {
		        for (Body b = _bodyList; b != null; b = b.GetNext())
		        {
                    Transform xf;
                    b.GetTransform(out xf);
			        xf.Position = b.GetWorldCenter();
			        DebugDraw.DrawTransform(ref xf);
		        }
	        }
        }

	    /// Query the world for all fixtures that potentially overlap the
	    /// provided AABB.
	    /// @param callback a user implemented callback class.
	    /// @param aabb the query box.
	    public void QueryAABB(Func<FixtureProxy, bool> callback, ref AABB aabb)
        {
            _queryAABBCallback = callback;
            _contactManager._broadPhase.Query(_queryAABBCallbackWrapper, ref aabb);
            _queryAABBCallback = null;
        }

        Func<FixtureProxy, bool> _queryAABBCallback;
        Func<int, bool> _queryAABBCallbackWrapper;

        bool QueryAABBCallbackWrapper(int proxyId)
        {
            FixtureProxy proxy = (FixtureProxy)_contactManager._broadPhase.GetUserData(proxyId);
            return _queryAABBCallback(proxy);
        }

        /// Ray-cast the world for all fixtures in the path of the ray. Your callback
	    /// controls whether you get the closest point, any point, or n-points.
	    /// The ray-cast ignores shapes that contain the starting point.
	    /// @param callback a user implemented callback class.
	    /// @param point1 the ray starting point
	    /// @param point2 the ray ending point
        public void RayCast(RayCastCallback callback, Vector2 point1, Vector2 point2)
        {
            RayCastInput input = new RayCastInput();
            input.maxFraction = 1.0f;
            input.p1 = point1;
            input.p2 = point2;

            _rayCastCallback = callback;
            _contactManager._broadPhase.RayCast(_rayCastCallbackWrapper, ref input);
            _rayCastCallback = null;
        }

        RayCastCallback _rayCastCallback;
        RayCastCallbackInternal _rayCastCallbackWrapper;

        float RayCastCallbackWrapper(ref RayCastInput input, int proxyId)
	    {
		    object userData = _contactManager._broadPhase.GetUserData(proxyId);
            FixtureProxy proxy = (FixtureProxy)userData;
            Fixture fixture = proxy.fixture;
            int index = proxy.childIndex;
		    RayCastOutput output;
		    bool hit = fixture.RayCast(out output, ref input, index);

		    if (hit)
		    {
			    float fraction = output.fraction;
			    Vector2 point = (1.0f - fraction) * input.p1 + fraction * input.p2;
                return _rayCastCallback(fixture, point, output.normal, fraction);
		    }

		    return input.maxFraction;
	    }

	    /// Get the world body list. With the returned body, use Body.GetNext to get
	    /// the next body in the world list. A null body indicates the end of the list.
	    /// @return the head of the world body list.
	    public Body GetBodyList()
        {
            return _bodyList;
        }

	    /// Get the world joint list. With the returned joint, use Joint.GetNext to get
	    /// the next joint in the world list. A null joint indicates the end of the list.
	    /// @return the head of the world joint list.
	    public Joint GetJointList()
        {
            return _jointList;
        }

	    /// Get the world contact list. With the returned contact, use Contact.GetNext to get
	    /// the next contact in the world list. A null contact indicates the end of the list.
	    /// @return the head of the world contact list.
	    /// @warning contacts are 
	    public Contact GetContactList()
        {
            return _contactManager._contactList;
        }

	    /// Enable/disable warm starting. For testing.
	    public bool WarmStarting { get; set; }

	    /// Enable/disable continuous physics. For testing.
	    public bool ContinuousPhysics { get; set; }

	    /// Get the number of broad-phase proxies.
	    public int ProxyCount 
        {  
            get
            {
                return _contactManager._broadPhase.ProxyCount;
            }
        }

	    /// Get the number of bodies.
	    public int BodyCount
        {
            get
            {
                return _bodyCount;
            }
        }

	    /// Get the number of joints.
	    public int JointCount
        {
            get
            {
                return _jointCount;
            }
        }
	    /// Get the number of contacts (each may have 0 or more contact points).
	    public int ContactCount
        {
            get
            {
                return _contactManager._contactCount;
            }
        }

	    /// Change the global gravity vector.
	    public Vector2 Gravity { get; set; }

	    /// Is the world locked (in the middle of a time step).
	    public bool IsLocked 
        { 
            get 
            { 
                return (_flags & WorldFlags.Locked) == WorldFlags.Locked; 
            }
            set 
            { 
                if (value)
                {
                    _flags |= WorldFlags.Locked;
                }
                else
                {
                    _flags &= ~WorldFlags.Locked;
                }
            }
        }

	    void Solve(ref TimeStep step)
        {
	        // Size the island for the worst case.
            _island.Reset(_bodyCount,
	                      _contactManager._contactCount,
	                      _jointCount,
	                      _contactManager.ContactListener);

	        // Clear all the island flags.
	        for (Body b = _bodyList; b != null; b = b._next)
	        {
		        b._flags &= ~BodyFlags.Island;
	        }
	        for (Contact c = _contactManager._contactList; c != null; c = c._next)
	        {
		        c._flags &= ~ContactFlags.Island;
	        }
	        for (Joint j = _jointList; j != null; j = j._next)
	        {
		        j._islandFlag = false;
	        }

	        // Build and simulate all awake islands.
            int stackSize = _bodyCount;
	        if (stackSize > stack.Length)
                stack = new Body[Math.Max(stack.Length * 2, stackSize)];

	        for (Body seed = _bodyList; seed != null; seed = seed._next)
	        {
		        if ((seed._flags & (BodyFlags.Island)) != BodyFlags.None)
		        {
			        continue;
		        }

		        if (seed.IsAwake() == false || seed.IsActive() == false)
		        {
			        continue;
		        }

                // The seed can be dynamic or kinematic.
                if (seed.GetType() == BodyType.Static)
                {
                    continue;
                }

		        // Reset island and stack.
		        _island.Clear();
		        int stackCount = 0;
		        stack[stackCount++] = seed;
		        seed._flags |= BodyFlags.Island;

		        // Perform a depth first search (DFS) on the raint graph.
		        while (stackCount > 0)
		        {
			        // Grab the next body off the stack and add it to the island.
			        Body b = stack[--stackCount];
                    Debug.Assert(b.IsActive() == true);
			        _island.Add(b);

			        // Make sure the body is awake.
                    b.SetAwake(true);
                    
			        // To keep islands as small as possible, we don't
			        // propagate islands across static bodies.
			        if (b.GetType() == BodyType.Static)
			        {
				        continue;
			        }

			        // Search all contacts connected to this body.
			        for (ContactEdge ce = b._contactList; ce != null; ce = ce.Next)
			        {
                        Contact contact = ce.Contact;

				        // Has this contact already been added to an island?
                        if ((contact._flags & ContactFlags.Island) != ContactFlags.None)
				        {
					        continue;
				        }

                        // Is this contact solid and touching?
                        if (!ce.Contact.IsEnabled() || !ce.Contact.IsTouching())
				        {
					        continue;
				        }

                        // Skip sensors.
                        bool sensorA = contact._fixtureA._isSensor;
                        bool sensorB = contact._fixtureB._isSensor;
                        if (sensorA || sensorB)
                        {
                            continue;
                        }

                        _island.Add(contact);
        				contact._flags |= ContactFlags.Island;

				        Body other = ce.Other;

				        // Was the other body already added to this island?
				        if ((other._flags & BodyFlags.Island) != BodyFlags.None)
				        {
					        continue;
				        }

				        Debug.Assert(stackCount < stackSize);
				        stack[stackCount++] = other;
				        other._flags |= BodyFlags.Island;
			        }

			        // Search all joints connect to this body.
			        for (JointEdge je = b._jointList; je != null; je = je.Next)
			        {
				        if (je.Joint._islandFlag == true)
				        {
					        continue;
				        }                        

				        Body other = je.Other;

                        // Don't simulate joints connected to inactive bodies.
                        if (other.IsActive() == false)
                        {
                            continue;
                        }

                        _island.Add(je.Joint);
                        je.Joint._islandFlag = true;

				        if ((other._flags & BodyFlags.Island) != BodyFlags.None)
				        {
					        continue;
				        }

				        Debug.Assert(stackCount < stackSize);
				        stack[stackCount++] = other;
				        other._flags |= BodyFlags.Island;
			        }
		        }

		        _island.Solve(ref step, Gravity, _allowSleep);

		        // Post solve cleanup.
		        for (int i = 0; i < _island._bodyCount; ++i)
		        {
			        // Allow static bodies to participate in other islands.
			        Body b = _island._bodies[i];
			        if (b.GetType() == BodyType.Static)
			        {
				        b._flags &= ~BodyFlags.Island;
			        }
		        }
	        }

	        // Synchronize fixtures, check for out of range bodies.
	        for (Body b = _bodyList; b != null; b = b.GetNext())
	        {
		        // If a body was not in an island then it did not move.
                if ((b._flags & BodyFlags.Island) != BodyFlags.Island)
		        {
			        continue;
		        }

                if (b.GetType() == BodyType.Static)
		        {
			        continue;
		        }

		        // Update fixtures (for broad-phase).
		        b.SynchronizeFixtures();
	        }

	        // Look for new contacts.
	        _contactManager.FindNewContacts();
        }

        // Advance a dynamic body to its first time of contact
        // and adjust the position to ensure clearance.
        void SolveTOI(Body body)
        {
	        // Find the minimum contact.
	        Contact toiContact = null;
	        float toi = 1.0f;
            Body toiOther = null;
	        bool found;
	        int count;
	        int iter = 0;

	        bool bullet = body.IsBullet;

	        // Iterate until all contacts agree on the minimum TOI. We have
	        // to iterate because the TOI algorithm may skip some intermediate
	        // collisions when objects rotate through each other.
	        do
	        {
		        count = 0;
		        found = false;
		        for (ContactEdge ce = body._contactList; ce != null; ce = ce.Next)
		        {
                    if (ce.Contact == toiContact)
                    {
                        continue;
                    }

			        Body other = ce.Other;
			        BodyType type = other.GetType();

			        // Only bullets perform TOI with dynamic bodies.
			        if (bullet == true)
			        {
				        // Bullets only perform TOI with bodies that have their TOI resolved.
				        if ((other._flags & BodyFlags.Toi) == 0)
				        {
					        continue;
				        }

                        // No repeated hits on non-static bodies
				        if (type != BodyType.Static && (ce.Contact._flags & ContactFlags.BulletHit) != 0)
				        {
						    continue;
				        }
			        }
			        else if (type == BodyType.Dynamic)
			        {
				        continue;
			        }

			        // Check for a disabled contact.
			        Contact contact = ce.Contact;
			        if (contact.IsEnabled() == false)
			        {
				        continue;
			        }

			        // Prevent infinite looping.
			        if (contact._toiCount > 10)
			        {
				        continue;
			        }

			        Fixture fixtureA = contact._fixtureA;
			        Fixture fixtureB = contact._fixtureB;
                    int indexA = contact._indexA;
			        int indexB = contact._indexB;

			        // Cull sensors.
			        if (fixtureA.IsSensor() || fixtureB.IsSensor())
			        {
				        continue;
			        }

			        Body bodyA = fixtureA._body;
			        Body bodyB = fixtureB._body;

			        // Compute the time of impact in interval [0, minTOI]
			        TOIInput input = new TOIInput();
			        input.proxyA.Set(fixtureA.GetShape(), indexA);
			        input.proxyB.Set(fixtureB.GetShape(), indexB);
			        input.sweepA = bodyA._sweep;
			        input.sweepB = bodyB._sweep;
			        input.tMax = toi;

			        TOIOutput output;
			        TimeOfImpact.CalculateTimeOfImpact(out output, ref input);

			        if (output.State == TOIOutputState.Touching && output.t < toi)
			        {
				        toiContact = contact;
				        toi = output.t;
                        toiOther = other;
				        found = true;
			        }

			        ++count;
		        }

		        ++iter;
	        } while (found && count > 1 && iter < 50);

	        if (toiContact == null)
	        {
                body.Advance(1.0f);
		        return;
	        }

	        Sweep backup = body._sweep;
	        body.Advance(toi);
            toiContact.Update(_contactManager.ContactListener);
	        if (toiContact.IsEnabled() == false)
	        {
		        // Contact disabled. Backup and recurse.
		        body._sweep = backup;
		        SolveTOI(body);
	        }

	        ++toiContact._toiCount;

	        // Update all the valid contacts on this body and build a contact island.
	        count = 0;
	        for (ContactEdge ce = body._contactList; (ce != null) && (count < Settings.b2_maxTOIContacts); ce = ce.Next)
	        {
		        Body other = ce.Other;
		        BodyType type = other.GetType();

                // Only perform correction with static bodies, so the
                // body won't get pushed out of the world.
		        if (type == BodyType.Dynamic)
		        {
			        continue;
		        }

		        // Check for a disabled contact.
		        Contact contact = ce.Contact;
		        if (contact.IsEnabled() == false)
		        {
			        continue;
		        }

		        Fixture fixtureA = contact._fixtureA;
		        Fixture fixtureB = contact._fixtureB;

		        // Cull sensors.
		        if (fixtureA.IsSensor() || fixtureB.IsSensor())
		        {
			        continue;
		        }

		        // The contact likely has some new contact points. The listener
		        // gives the user a chance to disable the contact.
		        if (contact != toiContact)
		        {
			        contact.Update(_contactManager.ContactListener);
		        }

		        // Did the user disable the contact?
		        if (contact.IsEnabled() == false)
		        {
			        // Skip this contact.
			        continue;
		        }

		        if (contact.IsTouching() == false)
		        {
			        continue;
		        }

		        _toiContacts[count] = contact;
		        ++count;
	        }

	        // Reduce the TOI body's overlap with the contact island.
	        _toiSolver.Initialize(_toiContacts, count, body);

	        float k_toiBaumgarte = 0.75f;
	        //bool solved = false;
	        for (int i = 0; i < 20; ++i)
	        {
		        bool contactsOkay = _toiSolver.Solve(k_toiBaumgarte);
		        if (contactsOkay)
		        {
			        //solved = true;
			        break;
		        }
	        }

            if (toiOther.GetType() != BodyType.Static)
	        {
		        toiContact._flags |= ContactFlags.BulletHit;
	        }
        }

        // Sequentially solve TOIs for each body. We bring each
        // body to the time of contact and perform some position correction.
        // Time is not conserved.
        void SolveTOI()
        {
	        // Prepare all contacts.
	        for (Contact c = _contactManager._contactList; c != null; c = c._next)
	        {
		        // Enable the contact
		        c._flags |= ContactFlags.Enabled;

		        // Set the number of TOI events for this contact to zero.
		        c._toiCount = 0;
	        }

	        // Initialize the TOI flag.
	        for (Body body = _bodyList; body != null; body = body._next)
	        {
		        // Kinematic, and static bodies will not be affected by the TOI event.
		        // If a body was not in an island then it did not move.
		        if ((body._flags & BodyFlags.Island) == 0 || body.GetType() == BodyType.Kinematic || body.GetType() == BodyType.Static)
		        {
			        body._flags |= BodyFlags.Toi;
		        }
		        else
		        {
			        body._flags &= ~BodyFlags.Toi;
		        }
	        }

	        // Collide non-bullets.
	        for (Body body = _bodyList; body != null; body = body._next)
	        {
		        if ((body._flags & BodyFlags.Toi) != BodyFlags.None)
		        {
			        continue;
		        }

		        if (body.IsBullet == true)
		        {
			        continue;
		        }

		        SolveTOI(body);

		        body._flags |= BodyFlags.Toi;
	        }

	        // Collide bullets.
	        for (Body body = _bodyList; body != null; body = body._next)
	        {
                if ((body._flags & BodyFlags.Toi) != BodyFlags.None)
		        {
			        continue;
		        }

		        if (body.IsBullet == false)
		        {
			        continue;
		        }

		        SolveTOI(body);

		        body._flags |= BodyFlags.Toi;
	        }
        }

	    void DrawJoint(Joint joint)
        {
	        Body b1 = joint.GetBodyA();
	        Body b2 = joint.GetBodyB();
            Transform xf1, xf2;
            b1.GetTransform(out xf1);
	        b2.GetTransform(out xf2);
	        Vector2 x1 = xf1.Position;
	        Vector2 x2 = xf2.Position;
	        Vector2 p1 = joint.GetAnchorA();
	        Vector2 p2 = joint.GetAnchorB();

	        Color color = new Color(0.5f, 0.8f, 0.8f);

	        switch (joint.JointType)
	        {
	        case JointType.Distance:
		        DebugDraw.DrawSegment(p1, p2, color);
		        break;

	        case JointType.Pulley:
		        {
			        PulleyJoint pulley = (PulleyJoint)joint;
			        Vector2 s1 = pulley.GetGroundAnchorA();
			        Vector2 s2 = pulley.GetGroundAnchorB();
			        DebugDraw.DrawSegment(s1, p1, color);
			        DebugDraw.DrawSegment(s2, p2, color);
			        DebugDraw.DrawSegment(s1, s2, color);
		        }
		        break;

	        case JointType.Mouse:
		        // don't draw this
		        break;

	        default:
		        DebugDraw.DrawSegment(x1, p1, color);
		        DebugDraw.DrawSegment(p1, p2, color);
		        DebugDraw.DrawSegment(x2, p2, color);
                break;
	        }
        }

	    void DrawShape(Fixture fixture, Transform xf, Color color)
        {
	        switch (fixture.ShapeType)
	        {
	        case ShapeType.Circle:
		        {
			        CircleShape circle = (CircleShape)fixture.GetShape();

			        Vector2 center = MathUtils.Multiply(ref xf, circle._p);
			        float radius = circle._radius;
			        Vector2 axis = xf.R.col1;

			        DebugDraw.DrawSolidCircle(center, radius, axis, color);
		        }
		        break;

	        case ShapeType.Polygon:
		        {
			        PolygonShape poly = (PolygonShape)fixture.GetShape();
			        int vertexCount = poly._vertexCount;
			        Debug.Assert(vertexCount <= Settings.b2_maxPolygonVertices);
			        FixedArray8<Vector2> vertices = new FixedArray8<Vector2>();

			        for (int i = 0; i < vertexCount; ++i)
			        {
				        vertices[i] = MathUtils.Multiply(ref xf, poly._vertices[i]);
			        }

			        DebugDraw.DrawSolidPolygon(ref vertices, vertexCount, color);
		        }
		        break;

            case ShapeType.Edge:
		        {
			        EdgeShape edge = (EdgeShape)fixture.GetShape();
			        Vector2 v1 = MathUtils.Multiply(ref xf, edge._vertex1);
			        Vector2 v2 = MathUtils.Multiply(ref xf, edge._vertex2);
			        DebugDraw.DrawSegment(v1, v2, color);
		        }
		        break;

	        case ShapeType.Loop:
		        {
			        LoopShape loop = (LoopShape)fixture.GetShape();
			        int count = loop._count;

			        Vector2 v1 = MathUtils.Multiply(ref xf, loop._vertices[count - 1]);
			        for (int i = 0; i < count; ++i)
			        {
				        Vector2 v2 = MathUtils.Multiply(ref xf, loop._vertices[i]);
				        DebugDraw.DrawSegment(v1, v2, color);
				        v1 = v2;
			        }
		        }
		        break;
	        }
        }

        TOISolver _toiSolver = new TOISolver();
        Contact[] _toiContacts = new Contact[Settings.b2_maxTOIContacts];
        internal Island _island = new Island();
        internal WorldFlags _flags;

	    internal ContactManager _contactManager = new ContactManager();
        internal Queue<Contact> _contactPool = new Queue<Contact>(256);

        internal Body _bodyList;
        internal Joint _jointList;

        internal int _bodyCount;
        internal int _jointCount;

        internal bool _allowSleep;

        internal Body _groundBody;

	    // This is used to compute the time step ratio to
	    // support a variable time step.
        internal float _inv_dt0;

        Body[] stack = new Body[64];
    }
}
