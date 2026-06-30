import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";
import { Type, type Static } from "typebox";
import { execFile } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { basename, dirname, join } from "node:path";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

const auditSchema = Type.Object({
	healthPath: Type.Optional(Type.String({ description: "Path to analyzer-health.md, relative to cwd unless absolute." })),
	verification: Type.Optional(
		Type.String({
			description: "How much verification to run. Default: none.",
			enum: ["none", "core", "analyzer", "codefix", "full"],
		}),
	),
	writeReport: Type.Optional(Type.Boolean({ description: "Write a markdown report under .pi/analyzer-health/. Default: true." })),
});

type AuditParams = Static<typeof auditSchema>;
type VerificationMode = NonNullable<AuditParams["verification"]>;

interface CommandResult {
	command: string;
	exitCode: number;
	stdout: string;
	stderr: string;
	durationMs: number;
}

interface ParsedHealth {
	path: string;
	lastRefreshed?: string;
	packageVersion?: string;
	baseCommit?: string;
	rules: RuleScore[];
	scoreIssues: string[];
}

interface RuleScore {
	rule: string;
	severity: string;
	importance: number;
	precision: number;
	testDepth: number;
	fixSafety: number;
	docs: number;
	release: number;
	score: number;
	expectedScore: number;
	priority: string;
}

interface AuditReport {
	cwd: string;
	health: ParsedHealth;
	git: {
		head?: string;
		base?: string;
		commitsSinceBase: string[];
		changedFiles: string[];
		classification: Record<string, string[]>;
	};
	packageVersions: Record<string, string>;
	verification: {
		mode: VerificationMode;
		results: CommandResult[];
		status: "not-run" | "pass" | "fail";
	};
	recommendations: string[];
	scoreRecommendations: Array<{ category: string; from?: number; to?: number; reason: string }>;
}

export default function analyzerHealthExtension(pi: ExtensionAPI) {
	pi.registerTool({
		name: "collect_analyzer_health_evidence",
		label: "Analyzer Health Evidence",
		description:
			"Collect deterministic git/package/verification evidence for rescoring analyzer-health.md. Does not edit files.",
		promptSnippet: "Collect analyzer-health.md audit evidence and score-math validation.",
		promptGuidelines: [
			"Use collect_analyzer_health_evidence before rescoring analyzer-health.md so score changes are tied to repeatable git, package, and verifier evidence.",
		],
		parameters: auditSchema,
		async execute(_toolCallId, params, signal, _onUpdate, ctx) {
			const report = await collectAudit(ctx.cwd, params, signal);
			const markdown = renderReport(report);
			let reportPath: string | undefined;

			if (params.writeReport ?? true) {
				reportPath = await writeAuditReport(ctx.cwd, markdown);
			}

			return {
				content: [
					{
						type: "text",
						text: reportPath ? `${markdown}\n\nReport written to: ${reportPath}` : markdown,
					},
				],
				details: { report, reportPath },
			};
		},
	});

	pi.registerCommand("analyzer-health-audit", {
		description: "Collect analyzer-health.md evidence and score-math recommendations",
		handler: async (args, ctx) => {
			const params = parseArgs(args);
			ctx.ui.notify("Collecting analyzer-health audit evidence...", "info");
			const report = await collectAudit(ctx.cwd, params, AbortSignal.timeout(20 * 60_000));
			const markdown = renderReport(report);
			const reportPath = params.writeReport === false ? undefined : await writeAuditReport(ctx.cwd, markdown);

			if (ctx.hasUI) {
				ctx.ui.notify(
					reportPath
						? `Analyzer health audit complete: ${reportPath}`
						: `Analyzer health audit complete: ${report.verification.status}`,
					report.verification.status === "fail" ? "warning" : "info",
				);
				ctx.ui.setEditorText(
					`Use this audit evidence to update analyzer-health.md. Do not change scores without citing evidence.\n\n${markdown}`,
				);
			}
		},
	});

	pi.registerCommand("analyzer-health-validate", {
		description: "Validate analyzer-health.md metadata and weighted score math",
		handler: async (_args, ctx) => {
			const health = await parseHealth(ctx.cwd, "analyzer-health.md");
			const head = await git(ctx.cwd, ["rev-parse", "--short", "HEAD"]);
			const issues = [...health.scoreIssues];
			if (health.baseCommit && head.exitCode === 0 && health.baseCommit !== head.stdout.trim()) {
				issues.push(`Base audited commit ${health.baseCommit} does not match HEAD ${head.stdout.trim()}.`);
			}

			if (issues.length === 0) {
				ctx.ui.notify("analyzer-health.md score math and HEAD metadata look current.", "info");
				return;
			}

			ctx.ui.notify(`analyzer-health.md has ${issues.length} validation issue(s).`, "warning");
			ctx.ui.setEditorText(`Fix analyzer-health.md validation issues:\n\n${issues.map((i) => `- ${i}`).join("\n")}`);
		},
	});
}

async function collectAudit(cwd: string, params: AuditParams, signal?: AbortSignal): Promise<AuditReport> {
	const verification = params.verification ?? "none";
	const health = await parseHealth(cwd, params.healthPath ?? "analyzer-health.md");
	const head = await git(cwd, ["rev-parse", "--short", "HEAD"], signal);
	const base = health.baseCommit;

	const commits = base
		? await git(cwd, ["log", "--oneline", "--no-merges", `${base}..HEAD`], signal)
		: { stdout: "", exitCode: 1 } as CommandResult;
	const changed = base
		? await git(cwd, ["diff", "--name-only", `${base}..HEAD`], signal)
		: { stdout: "", exitCode: 1 } as CommandResult;

	const changedFiles = lines(changed.stdout);
	const results = await runVerification(cwd, verification, signal);
	const status = verification === "none" ? "not-run" : results.every((r) => r.exitCode === 0) ? "pass" : "fail";
	const packageVersions = await collectPackageVersions(cwd);
	const classification = classifyFiles(changedFiles);
	const scoreRecommendations = recommendScoreChanges(health, status, changedFiles);
	const recommendations = recommendActions(health, head.stdout.trim(), status, changedFiles, scoreRecommendations);

	return {
		cwd,
		health,
		git: {
			head: head.exitCode === 0 ? head.stdout.trim() : undefined,
			base,
			commitsSinceBase: lines(commits.stdout),
			changedFiles,
			classification,
		},
		packageVersions,
		verification: { mode: verification, results, status },
		recommendations,
		scoreRecommendations,
	};
}

async function parseHealth(cwd: string, healthPath: string): Promise<ParsedHealth> {
	const path = healthPath.startsWith("/") ? healthPath : join(cwd, healthPath);
	const text = await readFile(path, "utf8");
	const lastRefreshed = match(text, /^Last refreshed:\s*(.+)$/m);
	const packageVersion = match(text, /^Package version:\s*`?([^`\n]+)`?$/m);
	const baseCommit = match(text, /^Base audited commit:\s*`?([^`\n]+)`?$/m);
	const rules = parseRuleRows(text);
	const scoreIssues = rules
		.filter((r) => Math.abs(r.score - r.expectedScore) > 0.005)
		.map((r) => `${r.rule} score is ${r.score.toFixed(2)} but rubric computes ${r.expectedScore.toFixed(2)}.`);

	return { path, lastRefreshed, packageVersion, baseCommit, rules, scoreIssues };
}

function parseRuleRows(text: string): RuleScore[] {
	return text
		.split("\n")
		.filter((line) => /^\| CFG\d{3} /.test(line))
		.map((line) => {
			const cells = line.split("|").slice(1, -1).map((cell) => cell.trim());
			const importance = numberCell(cells[2]);
			const precision = numberCell(cells[3]);
			const testDepth = numberCell(cells[4]);
			const fixSafety = numberCell(cells[5]);
			const docs = numberCell(cells[6]);
			const release = numberCell(cells[7]);
			const expectedScore = round2(
				importance * 0.25 + precision * 0.25 + testDepth * 0.2 + fixSafety * 0.15 + docs * 0.1 + release * 0.05,
			);
			return {
				rule: cells[0],
				severity: cells[1],
				importance,
				precision,
				testDepth,
				fixSafety,
				docs,
				release,
				score: numberCell(cells[8]),
				expectedScore,
				priority: cells[9],
			};
		});
}

async function runVerification(cwd: string, mode: VerificationMode, signal?: AbortSignal): Promise<CommandResult[]> {
	const commands: string[][] = [];
	if (mode === "core") {
		commands.push(["test", "tests/ConfigContraband.Core.Tests/ConfigContraband.Core.Tests.csproj", "--configuration", "Release"]);
	} else if (mode === "analyzer") {
		commands.push(["test", "tests/ConfigContraband.Tests/ConfigContraband.Tests.csproj", "--configuration", "Release", "--filter", "ConfigContrabandAnalyzerTests"]);
	} else if (mode === "codefix") {
		commands.push(["test", "tests/ConfigContraband.Tests/ConfigContraband.Tests.csproj", "--configuration", "Release", "--filter", "ConfigContrabandCodeFixTests"]);
	} else if (mode === "full") {
		commands.push(["test", "ConfigContraband.slnx", "--configuration", "Release"]);
		commands.push(["format", "ConfigContraband.slnx", "--verify-no-changes", "--no-restore", "--verbosity", "minimal"]);
	}

	const results: CommandResult[] = [];
	for (const args of commands) {
		const result = await run(cwd, "dotnet", args, signal);
		results.push(result);
		if (result.exitCode !== 0) break;
	}

	if (mode === "full") {
		results.push(await run(cwd, "git", ["diff", "--check"], signal));
	}

	return results;
}

async function collectPackageVersions(cwd: string): Promise<Record<string, string>> {
	const projectFiles = [
		"src/ConfigContraband/ConfigContraband.csproj",
		"src/ConfigContraband.Tool/ConfigContraband.Tool.csproj",
		"src/ConfigContraband.Core/ConfigContraband.Core.csproj",
	];
	const versions: Record<string, string> = {};
	for (const file of projectFiles) {
		try {
			const text = await readFile(join(cwd, file), "utf8");
			const version = match(text, /<Version>([^<]+)<\/Version>/);
			if (version) versions[file] = version;
		} catch {
			// Ignore missing project files; the report will simply omit them.
		}
	}
	return versions;
}

function classifyFiles(files: string[]): Record<string, string[]> {
	const groups: Record<string, string[]> = {
		analyzerSource: [],
		coreSource: [],
		toolSource: [],
		tests: [],
		docs: [],
		workflows: [],
		dependencies: [],
		other: [],
	};

	for (const file of files) {
		if (file.startsWith("src/ConfigContraband/") && file.endsWith(".cs")) groups.analyzerSource.push(file);
		else if (file.startsWith("src/ConfigContraband.Core/") && file.endsWith(".cs")) groups.coreSource.push(file);
		else if (file.startsWith("src/ConfigContraband.Tool/") && file.endsWith(".cs")) groups.toolSource.push(file);
		else if (file.startsWith("tests/")) groups.tests.push(file);
		else if (file.startsWith(".github/workflows/")) groups.workflows.push(file);
		else if (file.endsWith(".md")) groups.docs.push(file);
		else if (file.endsWith(".csproj") || basename(file).toLowerCase().includes("packages") || basename(file) === "global.json") groups.dependencies.push(file);
		else groups.other.push(file);
	}

	return Object.fromEntries(Object.entries(groups).filter(([, value]) => value.length > 0));
}

function recommendScoreChanges(health: ParsedHealth, status: AuditReport["verification"]["status"], changedFiles: string[]) {
	const recommendations: AuditReport["scoreRecommendations"] = [];
	if (status === "fail") {
		const releases = new Set(health.rules.map((r) => r.release));
		if (releases.size !== 1 || !releases.has(1)) {
			recommendations.push({
				category: "Release Readiness",
				from: releases.size === 1 ? [...releases][0] : undefined,
				to: 1,
				reason: "Verification failed; release should be blocked until the verifier is restored.",
			});
		}
	} else if (status === "pass") {
		const lowReleaseRules = health.rules.filter((r) => r.release < 5);
		if (lowReleaseRules.length > 0) {
			recommendations.push({
				category: "Release Readiness",
				from: Math.min(...lowReleaseRules.map((r) => r.release)),
				to: 5,
				reason: "Requested verification passed; release-readiness blocker appears resolved.",
			});
		}
	} else if (changedFiles.length > 0) {
		recommendations.push({
			category: "Audit Metadata",
			reason: "There are commits after the audited base; refresh Last refreshed/Base audited commit and decide whether verification evidence is needed.",
		});
	}
	return recommendations;
}

function recommendActions(
	health: ParsedHealth,
	head: string,
	status: AuditReport["verification"]["status"],
	changedFiles: string[],
	scoreRecommendations: AuditReport["scoreRecommendations"],
): string[] {
	const actions: string[] = [];
	if (health.baseCommit && head && health.baseCommit !== head) {
		actions.push(`Update Base audited commit from ${health.baseCommit} to ${head} if this audit becomes the new baseline.`);
	}
	if (health.scoreIssues.length > 0) actions.push("Fix weighted score math before committing analyzer-health.md.");
	if (changedFiles.length > 0) actions.push("Add an Improvement Changelog row summarizing the audited delta since the prior base.");
	if (status === "fail") actions.push("Treat verifier failure as P1 release-readiness work before rule expansion.");
	if (status === "not-run") actions.push("Run at least targeted verification before increasing Test Depth, Fix Safety, or Release Readiness scores.");
	if (scoreRecommendations.length > 0) actions.push("Apply score changes only with the evidence/reason listed in Score Recommendations.");
	if (actions.length === 0) actions.push("No health-file changes are recommended from the collected evidence.");
	return actions;
}

function renderReport(report: AuditReport): string {
	const changedSummary = Object.entries(report.git.classification)
		.map(([group, files]) => `- ${group}: ${files.length}`)
		.join("\n") || "- none";
	const verificationSummary = report.verification.results.length === 0
		? "- not run"
		: report.verification.results
				.map((r) => `- \`${r.command}\`: ${r.exitCode === 0 ? "PASS" : "FAIL"} (${Math.round(r.durationMs / 1000)}s)`)
				.join("\n");
	const scoreRecommendations = report.scoreRecommendations.length === 0
		? "- none"
		: report.scoreRecommendations
				.map((r) => `- ${r.category}: ${r.from === undefined ? "" : `${r.from} -> `}${r.to ?? "review"} — ${r.reason}`)
				.join("\n");
	const scoreIssues = report.health.scoreIssues.length === 0 ? "- none" : report.health.scoreIssues.map((i) => `- ${i}`).join("\n");

	return `# Analyzer Health Audit Evidence\n\n` +
		`- cwd: \`${report.cwd}\`\n` +
		`- health file: \`${report.health.path}\`\n` +
		`- last refreshed: ${report.health.lastRefreshed ?? "unknown"}\n` +
		`- package version in health: ${report.health.packageVersion ?? "unknown"}\n` +
		`- base audited commit: ${report.git.base ?? "unknown"}\n` +
		`- HEAD: ${report.git.head ?? "unknown"}\n\n` +
		`## Package Versions\n\n${formatMap(report.packageVersions)}\n\n` +
		`## Commits Since Base\n\n${formatList(report.git.commitsSinceBase)}\n\n` +
		`## Changed Files\n\n${changedSummary}\n\n` +
		`## Verification (${report.verification.mode})\n\nStatus: **${report.verification.status}**\n\n${verificationSummary}\n\n` +
		`## Score Math Issues\n\n${scoreIssues}\n\n` +
		`## Score Recommendations\n\n${scoreRecommendations}\n\n` +
		`## Recommended Actions\n\n${formatList(report.recommendations)}\n`;
}

async function writeAuditReport(cwd: string, markdown: string): Promise<string> {
	const dir = join(cwd, ".pi", "analyzer-health");
	await mkdir(dir, { recursive: true });
	const stamp = new Date().toISOString().replace(/[:.]/g, "-");
	const path = join(dir, `audit-${stamp}.md`);
	await writeFile(path, markdown, "utf8");
	return path;
}

function parseArgs(args: string): AuditParams {
	const parts = args.trim().split(/\s+/).filter(Boolean);
	const params: AuditParams = {};
	for (const part of parts) {
		if (["none", "core", "analyzer", "codefix", "full"].includes(part)) params.verification = part as VerificationMode;
		else if (part === "--no-write") params.writeReport = false;
		else if (part.startsWith("--health=")) params.healthPath = part.slice("--health=".length);
	}
	return params;
}

async function git(cwd: string, args: string[], signal?: AbortSignal): Promise<CommandResult> {
	return run(cwd, "git", args, signal);
}

async function run(cwd: string, file: string, args: string[], signal?: AbortSignal): Promise<CommandResult> {
	const start = Date.now();
	const command = `${file} ${args.join(" ")}`;
	try {
		const { stdout, stderr } = await execFileAsync(file, args, {
			cwd,
			signal,
			maxBuffer: 10 * 1024 * 1024,
		});
		return { command, exitCode: 0, stdout, stderr, durationMs: Date.now() - start };
	} catch (error: any) {
		return {
			command,
			exitCode: typeof error?.code === "number" ? error.code : 1,
			stdout: String(error?.stdout ?? ""),
			stderr: String(error?.stderr ?? error?.message ?? ""),
			durationMs: Date.now() - start,
		};
	}
}

function match(text: string, regex: RegExp): string | undefined {
	return regex.exec(text)?.[1]?.trim();
}

function lines(text: string): string[] {
	return text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
}

function numberCell(value: string): number {
	const parsed = Number(value.replace(/`/g, ""));
	return Number.isFinite(parsed) ? parsed : Number.NaN;
}

function round2(value: number): number {
	return Math.round(value * 100) / 100;
}

function formatList(items: string[]): string {
	return items.length === 0 ? "- none" : items.map((item) => `- ${item}`).join("\n");
}

function formatMap(map: Record<string, string>): string {
	const entries = Object.entries(map);
	return entries.length === 0 ? "- none" : entries.map(([key, value]) => `- ${key}: \`${value}\``).join("\n");
}
