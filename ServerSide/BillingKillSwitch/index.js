const { GoogleAuth } = require('google-auth-library');

exports.stopBilling = async (event) => {
  const pubsubMessage = event.data
    ? Buffer.from(event.data, 'base64').toString()
    : null;
  console.log(`Received billing alert payload: ${pubsubMessage}`);

  let data;
  try {
    data = JSON.parse(pubsubMessage);
  } catch (e) {
    console.error('Error parsing pubsub message:', e);
    return;
  }

  // Check if threshold >= 1.0 (meaning 100% of budget reached)
  if (data && data.alertThresholdExceeded >= 1.0) {
    console.log('Budget limit exceeded 100%. Unlinking billing...');
    
    const projectId = process.env.GOOGLE_CLOUD_PROJECT || 'acm-application-38298';
    const auth = new GoogleAuth({
      scopes: ['https://www.googleapis.com/auth/cloud-platform']
    });
    const client = await auth.getClient();
    const url = `https://cloudbilling.googleapis.com/v1/projects/${projectId}/billingInfo`;
    
    // Call the Cloud Billing API to unlink the project from its billing account
    const res = await client.request({
      url,
      method: 'PUT',
      data: { billingAccountName: '' } // Setting it to empty string unlinks billing
    });
    
    console.log('Successfully unlinked billing account:', res.data);
  } else {
    console.log('Budget is still within limits or threshold is below 100%. No action taken.');
  }
};
