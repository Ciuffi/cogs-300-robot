using System.Collections;
using System.Linq;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class Coginator : CogsAgent
{
    // ------------------BASIC MONOBEHAVIOR FUNCTIONS-------------------
    
    Animator animator;

    // Initialize values
    protected override void Start()
    {
        base.Start();
        AssignBasicRewards();
        animator = GetComponentInChildren<Animator>();
    }

    // For actual actions in the environment (e.g. movement, shoot laser)
    // that is done continuously
    protected override void FixedUpdate() {
        base.FixedUpdate();
        
        LaserControl();

        if (IsFrozen()) {
            if (!animator.GetBool("Frozen")) {
                animator.SetTrigger("hit");
            }
            animator.SetBool("Frozen", true);
        }else {
            animator.SetBool("Frozen", false);
        }
        // Movement based on DirToGo and RotateDir
        moveAgent(dirToGo, rotateDir);
    }


    private int getBallsInBase(bool enemy) {
        var team = GetTeam();
        var enemyTeam = GetTeam() == 1 ? 2 : 1;
        List<Target> inBaseTargets = new List<GameObject>(targets)
                                    .Select(x => x.GetComponent<Target>())
                                    .Where(x => x.GetInBase() != 0)
                                    .ToList();
        return inBaseTargets.Where(x => x.GetInBase() == (enemy ? team : enemyTeam)).ToList().Count;
    }

    private int getEnemyBalls() {
        return enemy.GetComponent<CogsAgent>().GetCarrying();
    }

    private bool doWeHaveMajority() {
        return GetCarrying() - targets.Length / 2 < 0;
    }
    
    // --------------------AGENT FUNCTIONS-------------------------

    // Get relevant information from the environment to effectively learn behavior
    public override void CollectObservations(VectorSensor sensor)
    {
        // Agent velocity in x and z axis 
        var localVelocity = transform.InverseTransformDirection(rBody.velocity);
        sensor.AddObservation(localVelocity.x);
        sensor.AddObservation(localVelocity.z);

        // Time remaning
        sensor.AddObservation(timer.GetComponent<Timer>().GetTimeRemaning());  

        // Agent's current rotation
        var localRotation = transform.rotation;
        sensor.AddObservation(transform.rotation.y);

        // Agent and home base's position
        sensor.AddObservation(this.transform.localPosition);
        sensor.AddObservation(baseLocation.localPosition);

        // for each target in the environment, add: its position, whether it is being carried,
        // and whether it is in a base
        foreach (GameObject target in targets){
            sensor.AddObservation(target.transform.localPosition);
            sensor.AddObservation(target.GetComponent<Target>().GetCarried());
            sensor.AddObservation(target.GetComponent<Target>().GetInBase());
        }
        
        // Whether the agent is frozen
        sensor.AddObservation(IsFrozen());
    }

    // For manual override of controls. This function will use keyboard presses to simulate output from your NN 
    public override void Heuristic(float[] actionsOut)
    {
        var discreteActionsOut = actionsOut;
        discreteActionsOut[0] = 0; //Simulated NN output 0
        discreteActionsOut[1] = 0; //....................1
        discreteActionsOut[2] = 0; //....................2
        discreteActionsOut[3] = 0; //....................3

        discreteActionsOut[4] = 0;

       
        if (Input.GetKey(KeyCode.UpArrow))
        {
            discreteActionsOut[0] = 1;
        }       
        if (Input.GetKey(KeyCode.DownArrow))
        {
            discreteActionsOut[0] = 2;
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            discreteActionsOut[1] = 1;
        }
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            discreteActionsOut[1] = 2;
        }
        

        //Shoot
        if (Input.GetKey(KeyCode.Space)){
            discreteActionsOut[2] = 1;
        }

        //GoToNearestTarget
        if (Input.GetKey(KeyCode.A)){
            discreteActionsOut[3] = 1;
        }
        if (Input.GetKey(KeyCode.Z)){
            discreteActionsOut[4] = 1;
        }

    }

        // What to do when an action is received (i.e. when the Brain gives the agent information about possible actions)
    public override void OnActionReceived(float[] act)
    {
        int forwardAxis = (int)act[0]; //NN output 0

        int rotateAxis = (int)act[1]; 
        int shootAxis = (int) act[2]; 
        int goToTargetAxis = (int) act[3];
        int goToBaseAxis = (int) act[4];

        MovePlayer(forwardAxis, rotateAxis, shootAxis, goToTargetAxis, goToBaseAxis);
        
        

    }


// ----------------------ONTRIGGER AND ONCOLLISION FUNCTIONS------------------------
    // Called when object collides with or trigger (similar to collide but without physics) other objects
    protected override void OnTriggerEnter(Collider collision)
    {
        

        
        if (collision.gameObject.CompareTag("HomeBase") && collision.gameObject.GetComponent<HomeBase>().team == GetTeam())
        {
            AddReward(rewardDict["one-ball-in-base"] * carriedTargets.Count);
            if (carriedTargets.Count > 0) { 
                Debug.Log(carriedTargets.Count);
                animator.SetTrigger("pickUp");
            }
        }
        base.OnTriggerEnter(collision);
    }

    protected override void OnCollisionEnter(Collision collision) 
    {
        

        //target is not in my base and is not being carried and I am not frozen
        if (collision.gameObject.CompareTag("Target") && collision.gameObject.GetComponent<Target>().GetInBase() != GetTeam() && collision.gameObject.GetComponent<Target>().GetCarried() == 0 && !IsFrozen())
        {
            AddReward(rewardDict["pick-up-one-ball"]);
            animator.SetTrigger("pickUp");
        }

        if (collision.gameObject.CompareTag("Wall"))
        {
            //Add rewards here
        }
        base.OnCollisionEnter(collision);
    }



    //  --------------------------HELPERS---------------------------- 
     private void AssignBasicRewards() {
        rewardDict = new Dictionary<string, float>();
        
        rewardDict.Add("frozen", -0.5f);
        rewardDict.Add("shooting-laser", 0f);
        rewardDict.Add("hit-enemy", 0.5f);
        rewardDict.Add("dropped-one-target", -0.3f);
        rewardDict.Add("dropped-targets", -0.5f);
        rewardDict.Add("one-ball-in-base", 0.7f);
        rewardDict.Add("pick-up-one-ball", 0.7f);
    }
    
    private void MovePlayer(int forwardAxis, int rotateAxis, int shootAxis, int goToTargetAxis, int goToBase)
    {
        bool running = false;
        dirToGo = Vector3.zero;
        rotateDir = Vector3.zero;

        Vector3 forward = transform.forward;
        Vector3 backward = -transform.forward;
        Vector3 right = transform.up;
        Vector3 left = -transform.up;

        //fowardAxis: 
            // 0 -> do nothing
            // 1 -> go forward
            // 2 -> go backward
        if (forwardAxis == 0){
            //do nothing. This case is not necessary to include, it's only here to explicitly show what happens in case 0
        }
        else if (forwardAxis == 1){
            dirToGo = forward;
            running = true;
        }
        else if (forwardAxis == 2){
            dirToGo = backward;
            running = true;
        }

        //rotateAxis: 
            // 0 -> do nothing
            // 1 -> go right
            // 2 -> go left
        if (rotateAxis == 0){
            //do nothing
        }else if (rotateAxis == 1) {
            rotateDir = right;
        }else if (rotateAxis == 2) {
            rotateDir = left;
        }
        

        //shoot
        if (shootAxis == 1){
            SetLaser(true);
            if (IsLaserOn() && !IsFrozen()) {
                animator.SetBool("Attacking", true);
            }else {
                animator.SetBool("Attacking", false);
            }
        }
        else {
            animator.SetBool("Attacking", false);
            SetLaser(false);
        }

        //go to the nearest target
        if (goToTargetAxis == 1){
            GoToNearestTarget();
            running = true;
            
        }
        if (goToBase == 1){
            GoToBase();
            running = true;
        }
        
        runAnimation(running);
    }

    private void runAnimation(bool running) {
        if (running) {
            if (carriedTargets.Count > 3) {
                animator.SetBool("Moving", false);
                animator.SetBool("Walking", true);
            }else {
                animator.SetBool("Moving", true);
                animator.SetBool("Walking", false);
            }
        }else {
            animator.SetBool("Moving", false);
            animator.SetBool("Walking", false);
        }
    }

    // Go to home base
    private void GoToBase(){
        TurnAndGo(GetYAngle(myBase));
    }

    // Go to the nearest target
    private void GoToNearestTarget(){
        GameObject target = GetNearestTarget();
        if (target != null){
            float rotation = GetYAngle(target);
            TurnAndGo(rotation);
        }        
    }

    // Rotate and go in specified direction
    private void TurnAndGo(float rotation){

        if(rotation < -5f){
            rotateDir = transform.up;
        }
        else if (rotation > 5f){
            rotateDir = -transform.up;
        }
        else {
            dirToGo = transform.forward;
        }
    }

    // return reference to nearest target
    protected GameObject GetNearestTarget(){
        float distance = 200;
        GameObject nearestTarget = null;
        foreach (var target in targets)
        {
            float currentDistance = Vector3.Distance(target.transform.localPosition, transform.localPosition);
            if (currentDistance < distance && target.GetComponent<Target>().GetCarried() == 0 && target.GetComponent<Target>().GetInBase() != team){
                distance = currentDistance;
                nearestTarget = target;
            }
        }
        return nearestTarget;
    }

    private float GetYAngle(GameObject target) {
        
       Vector3 targetDir = target.transform.position - transform.position;
       Vector3 forward = transform.forward;

      float angle = Vector3.SignedAngle(targetDir, forward, Vector3.up);
      return angle; 
        
    }
}
