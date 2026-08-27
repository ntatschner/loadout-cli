---
id: cloud.aws
kind: cloud
title: AWS
summary: IAM, regional behaviour and service limits on AWS.
dependencies:
  - 'aws-sdk'
  - 'boto3'
  - 'AWSSDK.'
task_phrases:
  - 'aws'
  - 'amazon web services'
  - 's3'
  - 'lambda'
  - 'iam'
---

## Cares about

IAM policy shape, and what is regional versus global.

## Working rules

- Grant the narrowest IAM policy that works, and prefer roles to keys.
- Be explicit about region; a resource in the wrong one is invisible rather than broken.
- Assume eventual consistency unless the service documents otherwise.
- Check service quotas before designing around throughput.

## Pitfalls

- A wildcard resource in an IAM policy because the specific ARN was awkward.
- S3 bucket policy and IAM policy disagreeing, with the deny winning silently.
- Credentials from the environment differing between local and deployed runs.

## Verify

Verify with the role the workload actually assumes.
