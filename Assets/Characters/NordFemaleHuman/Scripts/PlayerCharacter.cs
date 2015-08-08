using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Animator))]
public class PlayerCharacter : MonoBehaviour {
	[SerializeField] float m_MovingTurnSpeed = 360;
	[SerializeField] float m_StationaryTurnSpeed = 180;
	[SerializeField] float m_JumpPower = 12f;
	[Range(1f, 4f)][SerializeField] float m_GravityMultiplier = 2f;
	[SerializeField] float m_RunCycleLegOffset = 0.2f; //specific to the character in sample assets, will need to be modified to work with others
	[SerializeField] float m_WalkSpeedMultiplier = 1f;
	[SerializeField] float m_RunSpeedMultiplier = 1f;
	[SerializeField] float m_AnimSpeedMultiplier = 1f;
	[SerializeField] float m_GroundCheckDistance = 0.1f;

	public Text staminaText;
	[SerializeField] float m_Stamina = 30f;
	[SerializeField] float m_StaminaRecoveryRate = 0.4f;
	private float stamina = 0f;

	// player physical properties
	Rigidbody m_Rigidbody;
	Animator m_Animator;
	float m_OrigGroundCheckDistance;
	const float k_Half = 0.5f;
	float m_TurnAmount;
	float m_ForwardAmount;
	Vector3 m_GroundNormal;
	float m_CapsuleHeight;
	Vector3 m_CapsuleCenter;
	CapsuleCollider m_Capsule;

	// player states
	bool m_IsGrounded;
	bool m_isRunning;
	bool m_isWalking;
	bool m_Crouching;		// TODO
	bool m_isAttacked;		// TODO
	bool m_combatReady;		// TODO

	// debug alerts
	bool groundAlerted = false;

	// audio
	private AudioSource audioSource;
	public AudioClip[] floorStep;
	private float volLowRange = .5f;
	private float volHighRange = 1.0f;

	Vector3 m_moveDir;
	Vector3 m_normMoveDir;

	/***** GAME LOOP CALLBACKS *****/

	// unity's game loop callback start
	void Start() {
		m_Animator = GetComponent<Animator>();
		m_Rigidbody = GetComponent<Rigidbody>();
		m_Capsule = GetComponent<CapsuleCollider>();
		m_CapsuleHeight = m_Capsule.height;
		m_CapsuleCenter = m_Capsule.center;
		
		m_Rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
		m_OrigGroundCheckDistance = m_GroundCheckDistance;

		audioSource = GetComponent<AudioSource> ();

		stamina = m_Stamina;

		UpdateHUD ();
	}

	// unity's game loop callback update
	void Update () {
		if (Input.GetMouseButtonDown(0))
			Debug.Log("Pressed left click.");
		if(Input.GetMouseButtonDown(1))
			Debug.Log("Pressed right click.");
		if(Input.GetMouseButtonDown(2))
			Debug.Log("Pressed middle click.");
	}

	// we implement this function to override the default root motion.
	// this allows us to modify the positional speed before it's applied.
	void OnAnimatorMove() {
		if (m_IsGrounded && Time.deltaTime > 0) {
			//			Vector3 v = (m_Animator.deltaPosition * m_WalkSpeedMultiplier) / Time.deltaTime;
			Vector3 v;
			if (m_isRunning) {
				v = m_moveDir * m_RunSpeedMultiplier * m_ForwardAmount;
			}
			else {
				v = m_moveDir * m_WalkSpeedMultiplier * m_ForwardAmount;
			}
			
			// we preserve the existing y part of the current velocity.
			v.y = m_Rigidbody.velocity.y;
			m_Rigidbody.velocity = v;
		}
	}

	/***** ANIMATION EVENT CALLBACKS *****/

	// animation event callback on animation event for footstep sound effect
	// @param -1 for left leg, and 1 for right leg
	public void PlayFootStepSound(int leg) {
//		if (m_isWalking && !audioSource.isPlaying) {
//			audioSource.clip = floorStep[0];
//			audioSource.Play();
//		}
//		else if (!m_isWalking && audioSource.isPlaying) {
//			audioSource.Pause ();
//		}
		audioSource.clip = floorStep [0];
		audioSource.Play ();
	}

	/***** PLAYER CONTROLLER LOGIC *****/

	// convert the world relative moveInput vector into a local-relative
	// turn amount and forward amount required to head in the desired
	// direction.
	public void Move(Vector3 move, bool crouch, bool jump, bool run) {
		m_isRunning = run;
		m_moveDir = new Vector3(move.x, move.y, move.z);

		if (move.magnitude > 0f) {
			if (m_IsGrounded && !run)
				m_isWalking = true;
			else
				m_isWalking = false;
			if (move.magnitude > 1f) {
				move.Normalize ();
			} 
		}
		else {
			m_isWalking = false;
		}

		move = transform.InverseTransformDirection(move);
		CheckGroundStatus();
		move = Vector3.ProjectOnPlane(move, m_GroundNormal);
		m_TurnAmount = Mathf.Atan2(move.x, move.z);
		m_ForwardAmount = move.z;

		m_normMoveDir = new Vector3 (move.x, move.y, move.z);

		// apply turning rotation
		ApplyExtraTurnRotation();
		
		// control and velocity handling is different when grounded and airborne:
		if (m_IsGrounded) {
			HandleGroundedMovement(crouch, jump);
		}
		else {
			HandleAirborneMovement();
		}
		
		ScaleCapsuleForCrouching(crouch);
		PreventStandingInLowHeadroom();
		
		// send input and other state parameters to the animator
		UpdateAnimator(move);

		// update player's stats
		UpdateStats ();

		// update UI
		UpdateHUD ();
	}

	void SetStaminaText() {
		staminaText.text = "Stamina: " + stamina.ToString ();
	}

	void UpdateHUD() {
		SetStaminaText ();
	}

	void ScaleCapsuleForCrouching(bool crouch) {
		if (m_IsGrounded && crouch)
		{
			if (m_Crouching) return;
			m_Capsule.height = m_Capsule.height / 2f;
			m_Capsule.center = m_Capsule.center / 2f;
			m_Crouching = true;
		}
		else
		{
			Ray crouchRay = new Ray(m_Rigidbody.position + Vector3.up * m_Capsule.radius * k_Half, Vector3.up);
			float crouchRayLength = m_CapsuleHeight - m_Capsule.radius * k_Half;
			if (Physics.SphereCast(crouchRay, m_Capsule.radius * k_Half, crouchRayLength))
			{
				m_Crouching = true;
				return;
			}
			m_Capsule.height = m_CapsuleHeight;
			m_Capsule.center = m_CapsuleCenter;
			m_Crouching = false;
		}
	}
	
	void PreventStandingInLowHeadroom() {
		// prevent standing up in crouch-only zones
		if (!m_Crouching)
		{
			Ray crouchRay = new Ray(m_Rigidbody.position + Vector3.up * m_Capsule.radius * k_Half, Vector3.up);
			float crouchRayLength = m_CapsuleHeight - m_Capsule.radius * k_Half;
			if (Physics.SphereCast(crouchRay, m_Capsule.radius * k_Half, crouchRayLength))
			{
				m_Crouching = true;
			}
		}
	}

	// update the animator parameters
	// limit maximum walking speed or running speed by controlling the forward amount
	void UpdateAnimator(Vector3 move) {
		if (!Input.GetKey(KeyCode.LeftShift)) {
			m_ForwardAmount = Mathf.Min(0.75f, m_ForwardAmount);
		}

		m_Animator.SetFloat("Forward", m_ForwardAmount, 0.1f, Time.deltaTime);
		m_Animator.SetFloat("Turn", m_TurnAmount, 0.1f, Time.deltaTime);
		m_Animator.SetBool("Crouch", m_Crouching);
		m_Animator.SetBool("OnGround", m_IsGrounded);
		if (!m_IsGrounded) {
			m_Animator.SetFloat("Jump", m_Rigidbody.velocity.y);
		}
		
		// calculate which leg is behind, so as to leave that leg trailing in the jump animation
		// (This code is reliant on the specific run cycle offset in our animations,
		// and assumes one leg passes the other at the normalized clip times of 0.0 and 0.5)
		float runCycle =
			Mathf.Repeat(
				m_Animator.GetCurrentAnimatorStateInfo(0).normalizedTime + m_RunCycleLegOffset, 1);
		float jumpLeg = (runCycle < k_Half ? 1 : -1) * m_ForwardAmount;
		if (m_IsGrounded) {
			m_Animator.SetFloat("JumpLeg", jumpLeg);
		}

		if (m_IsGrounded && move.magnitude > 0) {
			m_Animator.speed = m_AnimSpeedMultiplier;
		}
		else {
			// don't use that while airborne
			m_Animator.speed = 1;
		}
	}

	void UpdateStats() {
		// update stamina
		// slowing the character down as stamina approaches zero TODO
		if (m_IsGrounded) {
			if (m_isRunning) {
				if (stamina > 0 && m_isRunning)
					stamina -= (m_ForwardAmount * Time.deltaTime);
			}
			else if (stamina < m_Stamina)
				stamina += (m_StaminaRecoveryRate * Time.deltaTime);
		}
	}
	
	void HandleAirborneMovement() {
		// apply extra gravity from multiplier:
		Vector3 extraGravityForce = (Physics.gravity * m_GravityMultiplier) - Physics.gravity;
		m_Rigidbody.AddForce(extraGravityForce);

		m_GroundCheckDistance = m_Rigidbody.velocity.y < 0 ? m_OrigGroundCheckDistance : 0.01f;
	}
	
	void HandleGroundedMovement(bool crouch, bool jump) {
		// check whether conditions are right to allow a jump:
		if (jump && !crouch && m_Animator.GetCurrentAnimatorStateInfo(0).IsName("Grounded"))
		{
			// jump!
			m_Rigidbody.velocity = new Vector3(m_Rigidbody.velocity.x, m_JumpPower, m_Rigidbody.velocity.z);
			m_IsGrounded = false;
			m_Animator.applyRootMotion = false;
			m_GroundCheckDistance = 0.1f;
			groundAlerted = false;
			Debug.Log ("Player has jumped");
		}
	}
	
	void ApplyExtraTurnRotation() {
		// help the character turn faster (this is in addition to root rotation in the animation)
		float turnSpeed = Mathf.Lerp(m_StationaryTurnSpeed, m_MovingTurnSpeed, m_ForwardAmount);
		transform.Rotate(0, m_TurnAmount * turnSpeed * Time.deltaTime, 0);
	}
	
	void CheckGroundStatus() {
		RaycastHit hitInfo;
		#if UNITY_EDITOR
		// helper to visualise the ground check ray in the scene view
		Debug.DrawLine(transform.position + (Vector3.up * 0.1f), transform.position + (Vector3.up * 0.1f) + (Vector3.down * m_GroundCheckDistance));
		#endif
		// 0.1f is a small offset to start the ray from inside the character
		// it is also good to note that the transform position in the sample assets is at the base of the character
		if (Physics.Raycast(transform.position + (Vector3.up * 0.1f), Vector3.down, out hitInfo, m_GroundCheckDistance)) {
			m_GroundNormal = hitInfo.normal;
			m_IsGrounded = true;
			m_Animator.applyRootMotion = true;
			if (!groundAlerted) {
				Debug.Log ("Player has grounded");
				groundAlerted = true;
			}
		}
		else {
			m_IsGrounded = false;
			m_GroundNormal = Vector3.up;
			m_Animator.applyRootMotion = false;
		}
	}
}
