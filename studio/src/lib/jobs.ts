// Running one command from a page and waiting for its answer: the job runs
// as a child of the daemon's own launcher, exactly as the line shown beside
// the button reads, and the page is told how it ended in that command's own
// words.
import { commands, type Job } from './api';

const settled = (job: Job) => job.state !== 'running';

/**
 * Runs `loadout <path> <words>` and waits for it to end. Throws with what the
 * command said where it did not succeed, so a refusal reads as the command's
 * own sentence. Each read of the job, from the one that started it on, is
 * handed to `seen`, for a page that shows the command's output as it arrives.
 */
export async function runJob(path: string, words: string[], patience = 120_000, seen?: (job: Job) => void): Promise<Job> {
  let job = await commands.run(path, words);
  const until = Date.now() + patience;
  seen?.(job);

  while (!settled(job) && Date.now() < until) {
    await new Promise((resolve) => setTimeout(resolve, 400));
    job = await commands.job(job.id);
    seen?.(job);
  }

  if (!settled(job)) {
    throw new Error(`'loadout ${path}' is still going. It carries on; the Jobs strip shows how it ends.`);
  }

  if (job.state !== 'done') {
    throw new Error((job.error ?? '').trim() || (job.output ?? '').trim() || `'loadout ${path}' ended with exit code ${job.exitCode}.`);
  }

  return job;
}
