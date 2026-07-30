# Launch plan

## Current status

Do not launch Workspace Recall broadly yet. The repository presentation and
community surfaces are ready, but there is no signed, clean-machine-tested
public package. The project remains source-only.

## Stage 1: trustworthy trial

Complete every item in [RELEASE_CHECKLIST.md](../../RELEASE_CHECKLIST.md),
including:

- a trusted code-signing or Microsoft Store route;
- a clean Windows 10 and Windows 11 acceptance pass;
- a privacy and security audit of the final artifact;
- an understandable install or extraction and removal path; and
- explicit approval before publication.

Do not describe an unsigned CI build as a release candidate for general users.

## Stage 2: small beta

Recruit approximately five to ten relevant Windows users after a trustworthy
package exists. Prioritize:

- multi-monitor Office workflows;
- AutoCAD and Revit workflows; and
- users who regularly reopen several documents and applications together.

Ask for feedback rather than stars. A useful beta result is:

- at least three confirmed capture-and-restore successes;
- concrete examples of **File identified**, **Program only**, and
  **Needs review** behavior; and
- actionable issues recorded with fictional, privacy-safe data.

Do not publish real layouts, paths, documents, window titles, screenshots, or
testimonials without explicit permission.

## Stage 3: staggered public launch

Use one primary channel at a time and leave several days between channels so
responses can be handled and traffic can be attributed.

1. **Show HN** — only when people can directly try the app. The maintainer
   should personally write the final submission and replies. Do not solicit
   votes or coordinated comments.
2. **Product Hunt** — prepare a complete page with the direct product URL,
   download route, gallery, maker attribution, and first comment. Ask people to
   try the product and give feedback, not to upvote.
3. **Relevant Reddit communities** — read each community's rules, disclose
   authorship, ask moderators when uncertain, and tailor the post to the
   community's problem. Do not mass cross-post or send unsolicited messages.

The [channel brief](CHANNEL_BRIEF.md) contains verified facts and boundaries
for the maintainer to use when writing each post.

## Stage 4: durable Windows discovery

After the public package is stable:

- prepare a WinGet manifest using the stable publisher-controlled download;
- consider Microsoft Store distribution;
- publish meaningful release notes; and
- maintain Discussions, issues, and compatibility documentation.

WinGet or Store submission is not a substitute for signing, package validation,
or clean-machine acceptance.
