using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Events;

public class BallPhysics : MonoBehaviour
{
    public float bounceForce = 10f; // Adjust for the desired bounce height
    public UnityEvent onPointScored; // Event to invoke when a point is scored

    private Rigidbody rb;
    private int bounceCount = 0;
    private float timeSinceLastBounce = 0f;
    private float bounceLimit = 2f; // Number of allowed bounces

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false; // Make sure the Rigidbody is not kinematic
    }

    void Update()
    {
        // Check if the ball has bounced too many times without being hit
        if (bounceCount >= bounceLimit)
        {
            ScorePoint();
        }

        // Reset bounce count after a certain time if not hit
        if (timeSinceLastBounce > 1f)
        {
            bounceCount = 0;
        }
        timeSinceLastBounce += Time.deltaTime;
    }

    void OnCollisionEnter(Collision collision)
    {
        // Check for collisions with the floor
        if (collision.gameObject.CompareTag("Floor"))
        {
            bounceCount++;
            timeSinceLastBounce = 0f; // Reset timer on bounce
            rb.AddForce(Vector3.up * bounceForce, ForceMode.Impulse); // Bounce effect
        }

        // Check for collision with rackets
        if (collision.gameObject.CompareTag("Racket"))
        {
            // Reset the bounce count if the ball is hit
            bounceCount = 0;
            timeSinceLastBounce = 0f;
        }
    }

    private void ScorePoint()
    {
        // Invoke the point scored event
        onPointScored.Invoke();
        // Reset the game state or ball position here as needed
        ResetBall();
    }

    private void ResetBall()
    {
        // Reset ball position and velocity
        transform.position = Vector3.zero; // or your desired reset position
        rb.velocity = Vector3.zero; // Stop movement
        bounceCount = 0; // Reset bounce count
        timeSinceLastBounce = 0f; // Reset timer
    }
}
