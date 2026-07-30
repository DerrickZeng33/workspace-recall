# GitHub stars and public exposure for Space Recorder

Research date: 30 July 2026

## Bottom line

Do not spend the project's launch moment yet. The highest-leverage move is to make Space Recorder safe and easy for a stranger to try, then promote it to a few highly relevant audiences with a clear demo and a genuine request for feedback. Stars should be the by-product of useful software and credible participation.

GitHub documents that stars help people revisit projects, discover related content, and influence many repository rankings and Explore; it also explicitly bans fake or automated stars, rank abuse, engagement markets, and incentivized inauthentic engagement ([GitHub on stars](https://docs.github.com/en/get-started/exploring-projects-on-github/saving-repositories-with-stars), [GitHub Acceptable Use](https://docs.github.com/en/site-policy/acceptable-use-policies/github-acceptable-use-policies)).

## Current conversion gaps

The live repository currently has a good descriptive sentence and MIT license, but has no topics, homepage, Discussions, release, fork, or star ([repository API](https://api.github.com/repos/DerrickZeng33/workspace-recall), [releases API](https://api.github.com/repos/DerrickZeng33/workspace-recall/releases)). Its README explicitly says there is no public binary, and its first screen contains no screenshot, demo, or download path ([current README](https://github.com/DerrickZeng33/workspace-recall#readme)). Its community profile is 57% and reports no contribution guide, code of conduct, or issue template ([community profile API](https://api.github.com/repos/DerrickZeng33/workspace-recall/community/profile)).

That means broad promotion would currently send prospective users to source and build instructions, not a product they can quickly evaluate. The official Show HN rules specifically ask makers to make projects easy to try, and Product Hunt asks for a direct live product URL and product media ([Show HN](https://news.ycombinator.com/showhn.html), [Product Hunt posting guide](https://help.producthunt.com/en/articles/479557-how-to-post-a-product)).

## Priorities, in order

### 1. Make installation trustworthy

Publish a real versioned release with concise release notes and an installable or portable Windows asset. GitHub Releases are designed to package software, notes, and binaries, and the API exposes asset download counts ([GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)).

For a Windows app, trust prompts are a conversion issue. Microsoft says unsigned files can trigger “Windows protected your PC” and must rebuild SmartScreen reputation for every version. It recommends signing every release or publishing through Microsoft Store; Store apps are Microsoft-signed and avoid SmartScreen download warnings ([SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)).

Recommended release gate:

- A clean Windows 10/11 machine can download, install or unpack, launch, capture, restore, and uninstall/remove the app.
- The README explains the expected trust prompt honestly until signing or Store distribution is solved.
- The release page names supported Windows and .NET requirements, known limitations, privacy behavior, and the exact artifact to download.

### 2. Turn the README into a product page

GitHub says a README is often the first item visitors see and should explain what the project does, why it is useful, how to start, where to get help, and who maintains it ([GitHub README guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes)).

Above the first scroll, show:

1. One outcome-led sentence: save and restore multi-monitor Windows workspaces, including verified document paths.
2. One clean screenshot or short GIF showing capture and restore.
3. A prominent **Download latest release** link and a 3-step quick start.
4. Supported Windows versions and an honest “early prototype” label.
5. The strongest differentiator: local-only, no telemetry, and explicit privacy boundaries.

Keep the detailed security caveats, but move them below the demo and quick start. After the user has seen the value, a small “If this is useful, star the repository to save it for later” request is legitimate; do not make access, support, or rewards conditional on starring.

Create a 1280×640 social preview using the same visual and promise. GitHub says this image identifies the project when repository links are shared; it does not claim a ranking benefit ([social preview guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/customizing-your-repositorys-social-media-preview)).

### 3. Add documented GitHub discovery surfaces

GitHub's default repository search examines the name, description, and topics. README text is included only when a search uses `in:readme` ([repository search documentation](https://docs.github.com/en/search-github/searching-on-github/searching-for-repositories)). Therefore:

- Keep literal audience/problem terms in the description.
- Add a focused topic set such as `windows`, `desktop-app`, `wpf`, `dotnet`, `productivity`, `window-management`, `multi-monitor`, and `privacy-first`. Add `revit` or `autocad` only if those users are a deliberate audience.
- Verify the repository appears in each chosen `topic:` search. GitHub permits up to 20 topics, but relevance matters more than filling the quota ([topic guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/classifying-your-repository-with-topics)).
- Pin the repository on the maintainer's profile so profile visitors can quickly find it ([GitHub profile pins](https://docs.github.com/en/account-and-profile/how-tos/profile-customization/pinning-items-to-your-profile)).

Complete the community profile with a short CONTRIBUTING guide, issue forms, and an enforceable code of conduct. Add one or two real, self-contained `good first issue` tasks only when they are genuinely ready; GitHub documents that this label can increase the likelihood that approachable issues are surfaced ([community profiles](https://docs.github.com/en/communities/setting-up-your-project-for-healthy-contributions/about-community-profiles-for-public-repositories), [`good first issue`](https://docs.github.com/en/communities/setting-up-your-project-for-healthy-contributions/encouraging-helpful-contributions-to-your-project-with-labels)).

Enable Discussions before launch and pin a welcome/feedback thread. GitHub positions Discussions as the place for community conversation and Q&A that is not scoped work ([GitHub Discussions](https://docs.github.com/en/discussions/collaborating-with-your-community-using-discussions/about-discussions)).

### 4. Use Windows-native distribution for durable exposure

After the installer is stable:

- Submit a WinGet manifest. Microsoft says accepted manifests become discoverable to the WinGet client and make the app available to everyone; submissions must install and uninstall correctly, support non-interactive installation, and use a publisher-controlled installer URL ([WinGet submission](https://learn.microsoft.com/en-us/windows/package-manager/package/repository)).
- Consider an MSIX Microsoft Store submission. Microsoft recommends the Store for most developers because it provides broad discovery, trusted installation, Microsoft signing for MSIX, and managed updates ([Windows distribution paths](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path)).

These are durable acquisition channels for Windows users, not one-day launch spikes.

## Launch sequence

1. **Release first:** ship the tryable version, screenshot/GIF, social preview, topics, community files, Discussions, and clean-machine verification.
2. **Small beta:** ask a handful of relevant Windows users to install it and report friction. Ask for feedback, not stars.
3. **One channel at a time:** stagger channels by several days so the maintainer can answer every substantive response and traffic is easier to attribute. This spacing is a measurement recommendation, not an algorithm claim.
4. **Show HN:** only when the app is directly tryable. Use a factual title such as `Show HN: Space Recorder – save and restore Windows workspace layouts`; explain the problem, why it was built, technical/privacy decisions, and limitations. Do not solicit votes or comments. HN also now asks users not to post AI-generated or AI-edited comments, so the maker should write the final submission and replies personally ([Show HN rules](https://news.ycombinator.com/showhn.html), [HN guidelines](https://news.ycombinator.com/newsguidelines.html)).
5. **Product Hunt:** prepare the listing as a draft with direct download/product URL, tagline, gallery/demo, maker attribution, and a substantive maker first comment. Product Hunt explicitly says to ask people to visit and comment, not to upvote, and rejects paid or artificial traffic ([launch guide](https://www.producthunt.com/launch), [how to post](https://help.producthunt.com/en/articles/479557-how-to-post-a-product)).
6. **Reddit:** choose only communities where workspace restoration, Windows utilities, WPF, CAD, Revit, or AutoCAD is directly relevant. Read each community's rules, disclose authorship, tailor the post to the community's problem, and ask moderators if uncertain. Reddit prohibits repetitive mass posting and unsolicited engagement ([Reddit spam policy](https://support.reddithelp.com/hc/en-us/articles/360043504051-Spam)).
7. **Sustain:** publish meaningful releases, answer Discussions promptly, turn repeated questions into documentation, and share technical write-ups that are useful even to people who never install the app.

## Minimal measurement loop

GitHub Traffic provides repository views, unique visitors, full clones, top referrers, and popular content for only the previous 14 days, so snapshot it at least weekly ([traffic graph](https://docs.github.com/en/repositories/viewing-activity-and-data-for-your-repository/viewing-traffic-to-a-repository), [traffic API](https://docs.github.com/en/rest/metrics/traffic)). Track:

| Funnel stage | Metric |
| --- | --- |
| Exposure | Unique repository visitors and external referrer mix |
| Evaluation | README/release page views and full clones |
| Activation proxy | Release-asset download delta or Store acquisitions |
| Interest | New legitimate stars and forks |
| Community | User issues, Discussion participants, outside contributors, response time |

For each launch window, record the baseline and the 1-day, 7-day, and 14-day values. `new stars / unique visitors` and `new downloads / unique visitors` can be used as directional conversion proxies, but they are maintainer-defined estimates, not GitHub metrics; downloads may repeat and visitor windows can overlap. The Releases API exposes cumulative asset download counts ([release API](https://docs.github.com/en/rest/releases/releases)).

Do not add invasive in-app telemetry merely to optimize stars. GitHub's aggregate traffic, release downloads, Store/WinGet data, and voluntary user feedback are consistent with the project's privacy-first positioning.

## Non-negotiable integrity rules

Never buy or exchange stars, automate starring, use multiple accounts, offer giveaways or access for stars, scrape stargazers for outreach, coordinate voting, or mass-DM potential users. GitHub classifies automated starring, rank abuse, secondary engagement markets, and incentivized inauthentic engagement as prohibited activity ([GitHub Acceptable Use](https://docs.github.com/en/site-policy/acceptable-use-policies/github-acceptable-use-policies)). Hacker News forbids solicited votes, Product Hunt forbids direct upvote requests, and Reddit forbids mass or unsolicited promotion.

The safest promotion test is simple: every post should still be useful and honest if nobody stars the repository.
