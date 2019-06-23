using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

[DisableAutoCreation]
public class MoveSystem : ComponentSystem
{
    protected override void OnUpdate()
    {   

        Entities.ForEach((ref PhysicsVelocity velocity) =>
        {
           
            var deltaTime = Time.fixedDeltaTime;

            Vector3 desiredVelocity = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));

            Vector3 currentVelocity = new Vector3(velocity.Linear.x, 0, velocity.Linear.z);

            //velocity.Linear *= desiredVelocity;

            //velocity.Linear += new Unity.Mathematics.float3(0, 1, 0);
            

           

        });
    }
}

public class PhysicsTesting : MonoBehaviour
{
    private MoveSystem System;

    public void Start()
    {
        System = World.Active.GetOrCreateSystem<MoveSystem>();
    }

    public void FixedUpdate()
    {
       // System.Update();   
    }
}
