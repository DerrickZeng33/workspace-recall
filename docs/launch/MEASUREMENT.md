# Growth measurement

Space Recorder does not add in-app analytics or telemetry for promotion.
Measure repository-level interest through GitHub's aggregate traffic and
release data.

## Capture a snapshot

Install and authenticate GitHub CLI, then run:

```powershell
.\scripts\capture-github-growth.ps1
```

The script saves a timestamped JSON snapshot under
`artifacts\metrics`. That directory is ignored by Git and remains inside the
project. The snapshot contains repository counts, aggregate 14-day traffic,
top referrers, popular paths, and release-asset download counts. It does not
read Space Recorder layouts or application data.

Capture a baseline immediately before each outreach channel and compare it
after one, seven, and fourteen days.

## Minimum scorecard

| Funnel stage | Evidence |
| --- | --- |
| Exposure | Unique repository visitors and referrer mix |
| Evaluation | Popular repository paths and full clones |
| Trial intent | Signed release-asset downloads, once available |
| Interest | New legitimate stars and forks |
| Community | User issues, Discussions, and outside contributors |

`new stars / unique visitors` and `new downloads / unique visitors` may be used
as directional conversion estimates. They are not GitHub-defined metrics,
visitor windows can overlap, and asset downloads may include repeats.

## Interpretation

- Low qualified traffic suggests a distribution problem.
- Traffic without stars or clones suggests a positioning or trust problem.
- Stars without downloads suggest that the idea is interesting but the trial
  path is blocked.
- Downloads without successful feedback suggest onboarding or reliability
  problems.

Do not buy, exchange, automate, or incentivize stars. Do not scrape stargazers
for unsolicited outreach.
