import fs from 'fs';
import path from 'path';
import axios from 'axios';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Configuration
const BASE_URL = process.env.BASE_URL || 'https://triviup-backend-production.up.railway.app';
const RESULTS_DIR = path.join(__dirname, 'results');

// Ensure results directory exists
if (!fs.existsSync(RESULTS_DIR)) {
  fs.mkdirSync(RESULTS_DIR, { recursive: true });
}

// Global variables storage
let variables = {
  baseUrl: BASE_URL,
  timestamp: Date.now(),
  testUsername: 'bruno_test',
  testEmail: 'bruno_test@test.com',
  testPassword: 'password123',
  testUsername2: 'bruno_test2',
  testEmail2: 'bruno_test2@test.com',
  authToken: '',
  userId: '',
  adminToken: '',
  adminId: '',
  newUsername: '',
  quizId: '',
  roomCode: ''
};

// Test results
const testResults = [];

// Parse .bru file content
function parseBruFile(content) {
  const lines = content.split('\n');
  let section = 'meta';
  const request = { meta: {}, headers: {}, body: {}, auth: null };
  let jsonDepth = 0;
  let bodySectionClosed = false;

  for (const line of lines) {
    const trimmedLine = line.trim();

    if (trimmedLine === 'meta {' || trimmedLine === 'get {' ||
        trimmedLine === 'post {' || trimmedLine === 'put {' ||
        trimmedLine === 'delete {' || trimmedLine === 'patch {') {
      section = trimmedLine.replace(/ {/g, '');
      if (['get', 'post', 'put', 'delete', 'patch'].includes(section)) {
        request.httpMethod = section.toUpperCase();
      }
      continue;
    }

    if ((trimmedLine === 'body:json {' || trimmedLine === 'body {')) {
      section = trimmedLine.replace(/ {/g, '');
      jsonDepth = 0;
      bodySectionClosed = false;
      continue;
    }

    if (bodySectionClosed) {
      continue;
    }

    if (section === 'meta') {
      const colonIndex = trimmedLine.indexOf(':');
      if (colonIndex > 0) {
        const key = trimmedLine.substring(0, colonIndex).trim();
        const value = trimmedLine.substring(colonIndex + 1).trim().replace(/"/g, '');
        request.meta[key] = value;
      }
    } else if (['get', 'post', 'put', 'delete', 'patch'].includes(section)) {
      if (trimmedLine.startsWith('url:')) {
        request.url = trimmedLine.substring(4).trim();
      } else if (trimmedLine.startsWith('body:')) {
        request.bodyType = trimmedLine.substring(5).trim();
      } else if (trimmedLine === 'auth: bearer') {
        request.auth = 'bearer';
      } else if (trimmedLine === 'auth: none') {
        request.auth = 'none';
      }
    } else if (section === 'headers') {
      const colonIndex = trimmedLine.indexOf(':');
      if (colonIndex > 0) {
        const key = trimmedLine.substring(0, colonIndex).trim();
        const value = trimmedLine.substring(colonIndex + 1).trim();
        request.headers[key] = value;
      }
    } else if (section === 'body:json' || section === 'body') {
      let beforeDepth = jsonDepth;
      for (const char of trimmedLine) {
        if (char === '{') jsonDepth++;
        if (char === '}') jsonDepth--;
      }

      if (trimmedLine === '}' && beforeDepth <= 1) {
        if (beforeDepth === 0) {
          bodySectionClosed = true;
          continue;
        }
      }

      if (trimmedLine && !trimmedLine.startsWith('//')) {
        request.bodyText = (request.bodyText || '') + trimmedLine + '\n';
      }
    }
  }

  request.url = substituteVariables(request.url || '');
  return request;
}

function substituteVariables(str) {
  return str.replace(/\{\{(\w+)\}\}/g, (match, varName) => {
    return variables[varName] !== undefined ? variables[varName] : match;
  });
}

function parseBody(bodyText) {
  if (!bodyText) return null;
  try {
    let processedBody = substituteVariables(bodyText);
    return JSON.parse(processedBody);
  } catch (e) {
    console.error('Error parsing body:', e.message);
    return null;
  }
}

async function runTest(folderPath, fileName) {
  const filePath = path.join(folderPath, fileName);
  const content = fs.readFileSync(filePath, 'utf-8');
  const request = parseBruFile(content);

  const testName = request.meta.name || fileName.replace('.bru', '');
  const method = request.httpMethod || 'GET';

  console.log(`\n[${request.meta.seq || '?'}] Running: ${testName}`);

  const result = {
    name: testName,
    folder: path.basename(folderPath),
    method: method,
    url: request.url,
    status: 0,
    passed: false,
    duration: 0,
    expectedStatus: parseInt(request.meta['res.status']) || 200,
    actualStatus: 0,
    error: null,
    response: null
  };

  const startTime = Date.now();

  try {
    const config = {
      method: method,
      url: request.url,
      headers: {
        'Content-Type': 'application/json',
        ...request.headers
      },
      validateStatus: () => true,
      timeout: 30000
    };

    if (request.auth === 'bearer') {
      // Use adminToken for admin folder tests, authToken for others
      const token = folderPath.includes('admin') ? variables.adminToken : variables.authToken;
      config.headers['Authorization'] = `Bearer ${token}`;
    }

    if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method) && request.bodyText) {
      config.data = parseBody(request.bodyText);
    }

    const response = await axios(config);
    result.duration = Date.now() - startTime;
    result.actualStatus = response.status;
    result.response = response.data;

    result.passed = response.status === result.expectedStatus;

    if (request.meta['body.token'] === 'exists' && !response.data?.token) {
      result.passed = false;
      result.error = 'Token not found in response';
    }

    if (request.meta['body.user.id'] === 'exists' && !response.data?.user?.id) {
      result.passed = false;
      result.error = 'User ID not found in response';
    }

    if (response.data?.token) {
      if (fileName.includes('admin-signin')) {
        variables.adminToken = response.data.token;
        variables.adminId = response.data.user.id.toString();
      } else {
        variables.authToken = response.data.token;
        variables.userId = response.data.user.id.toString();
      }
    }

    if (response.data?.id && !variables.quizId) {
      variables.quizId = response.data.id.toString();
    }

    if (response.data?.roomCode) {
      variables.roomCode = response.data.roomCode;
    }

    console.log(`  ${result.passed ? '✓' : '✗'} ${response.status} (${result.duration}ms)`);

  } catch (error) {
    result.duration = Date.now() - startTime;
    result.actualStatus = 0;
    result.passed = false;
    result.error = error.message;
    console.log(`  ✗ ERROR: ${error.message}`);
  }

  testResults.push(result);
  return result;
}

async function runFolderTests(folderPath) {
  const files = fs.readdirSync(folderPath)
    .filter(f => f.endsWith('.bru'))
    .sort();

  for (const file of files) {
    await runTest(folderPath, file);
  }
}

function generateHtmlReport() {
  const passed = testResults.filter(r => r.passed).length;
  const failed = testResults.filter(r => !r.passed).length;
  const total = testResults.length;

  const html = `<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>TriviUp API Test Report</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f5f5f5; padding: 20px; }
    .container { max-width: 1200px; margin: 0 auto; }
    h1 { color: #333; margin-bottom: 20px; }
    .summary { display: flex; gap: 20px; margin-bottom: 30px; }
    .stat { background: white; padding: 20px 30px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
    .stat.total { border-left: 4px solid #666; }
    .stat.passed { border-left: 4px solid #22c55e; }
    .stat.failed { border-left: 4px solid #ef4444; }
    .stat-value { font-size: 32px; font-weight: bold; }
    .stat-label { color: #666; font-size: 14px; margin-top: 4px; }
    .percentage { font-size: 14px; color: #666; margin-top: 8px; }
    .folder { margin-bottom: 30px; }
    .folder-title { font-size: 18px; font-weight: 600; color: #333; margin-bottom: 12px; padding-bottom: 8px; border-bottom: 2px solid #e5e5e5; }
    .tests { background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
    .test { display: flex; align-items: center; padding: 12px 16px; border-bottom: 1px solid #f0f0f0; }
    .test:last-child { border-bottom: none; }
    .test:hover { background: #fafafa; }
    .test-icon { font-size: 18px; margin-right: 12px; }
    .test.passed .test-icon { color: #22c55e; }
    .test.failed .test-icon { color: #ef4444; }
    .test-info { flex: 1; }
    .test-name { font-weight: 500; color: #333; }
    .test-url { font-size: 12px; color: #888; margin-top: 2px; }
    .test-meta { display: flex; gap: 16px; font-size: 12px; color: #666; }
    .test-status { font-weight: 600; padding: 4px 8px; border-radius: 4px; }
    .test-status.passed { background: #dcfce7; color: #166534; }
    .test-status.failed { background: #fee2e2; color: #991b1b; }
    .test-error { font-size: 12px; color: #ef4444; margin-top: 4px; }
    .timestamp { text-align: center; color: #888; font-size: 12px; margin-top: 30px; }
  </style>
</head>
<body>
  <div class="container">
    <h1>🧪 TriviUp API Test Report</h1>

    <div class="summary">
      <div class="stat total">
        <div class="stat-value">${total}</div>
        <div class="stat-label">Total Tests</div>
      </div>
      <div class="stat passed">
        <div class="stat-value">${passed}</div>
        <div class="stat-label">Passed</div>
        <div class="percentage">${total > 0 ? ((passed/total)*100).toFixed(1) : 0}%</div>
      </div>
      <div class="stat failed">
        <div class="stat-value">${failed}</div>
        <div class="stat-label">Failed</div>
        <div class="percentage">${total > 0 ? ((failed/total)*100).toFixed(1) : 0}%</div>
      </div>
    </div>

    ${generateFolderSections()}

    <div class="timestamp">
      Generated: ${new Date().toLocaleString('es-ES')}
    </div>
  </div>
</body>
</html>`;

  return html;
}

function generateFolderSections() {
  const folders = {};

  for (const result of testResults) {
    if (!folders[result.folder]) {
      folders[result.folder] = [];
    }
    folders[result.folder].push(result);
  }

  let html = '';

  for (const [folderName, results] of Object.entries(folders)) {
    html += `
    <div class="folder">
      <div class="folder-title">${folderName.toUpperCase()}</div>
      <div class="tests">
        ${results.map(r => `
        <div class="test ${r.passed ? 'passed' : 'failed'}">
          <div class="test-icon">${r.passed ? '✓' : '✗'}</div>
          <div class="test-info">
            <div class="test-name">${r.name}</div>
            <div class="test-url">${r.method} ${r.url}</div>
            ${r.error ? `<div class="test-error">Error: ${r.error}</div>` : ''}
          </div>
          <div class="test-meta">
            <span class="test-status ${r.passed ? 'passed' : 'failed'}">${r.actualStatus || 'ERR'}</span>
            <span>${r.duration}ms</span>
          </div>
        </div>
        `).join('')}
      </div>
    </div>`;
  }

  return html;
}

async function main() {
  console.log('═══════════════════════════════════════════════════════════════');
  console.log('  TriviUp API Test Runner');
  console.log('═══════════════════════════════════════════════════════════════');
  console.log(`\n📡 Base URL: ${BASE_URL}`);
  console.log(`📁 Test Folder: ${__dirname}`);

  const brunoPath = __dirname;

  try {
    console.log('\n⏳ Checking if backend is running...');
    await axios.post(`${BASE_URL}/auth/signin`, {}, { timeout: 10000 });
    console.log('✅ Backend is running');
  } catch (error) {
    if (error.response) {
      console.log('✅ Backend is running (responding)');
    } else {
      console.error('❌ Backend is not running at', BASE_URL);
      console.error('   Error:', error.message);
      process.exit(1);
    }
  }

  const folders = fs.readdirSync(brunoPath)
    .filter(f => {
      const fullPath = path.join(brunoPath, f);
      return fs.statSync(fullPath).isDirectory() &&
             !f.startsWith('.') &&
             !f.startsWith('node_') &&
             f !== 'environments' &&
             f !== 'results';
    });

  for (const folder of folders.sort()) {
    const folderPath = path.join(brunoPath, folder);
    console.log(`\n📂 Folder: ${folder}`);
    console.log('─'.repeat(50));

    if (fs.existsSync(path.join(folderPath, '.disabled'))) {
      console.log('  ⏭️  Skipped (folder disabled)');
      continue;
    }

    await runFolderTests(folderPath);
  }

  console.log('\n═══════════════════════════════════════════════════════════════');
  console.log('  Generating Report...');

  const html = generateHtmlReport();
  const reportPath = path.join(RESULTS_DIR, `report_${Date.now()}.html`);
  fs.writeFileSync(reportPath, html, 'utf-8');

  console.log(`\n📊 Report generated: ${reportPath}`);
  console.log('\n═══════════════════════════════════════════════════════════════');
  console.log('  SUMMARY');
  console.log('═══════════════════════════════════════════════════════════════');
  console.log(`  Total:  ${testResults.length}`);
  console.log(`  Passed: ${testResults.filter(r => r.passed).length}`);
  console.log(`  Failed: ${testResults.filter(r => !r.passed).length}`);

  if (testResults.some(r => !r.passed)) {
    console.log('\n❌ Some tests failed!');
    process.exit(1);
  } else {
    console.log('\n✅ All tests passed!');
  }
}

main().catch(console.error);
