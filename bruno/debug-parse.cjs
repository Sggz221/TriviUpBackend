const fs = require('fs');

function parseBruFile(content) {
  const lines = content.split('\n');
  let section = 'meta';
  const request = { meta: {}, headers: {}, body: {}, auth: null };
  let jsonDepth = 0;

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
    } else if (section === 'body:json' || section === 'body') {
      let beforeDepth = jsonDepth;
      for (const char of trimmedLine) {
        if (char === '{') jsonDepth++;
        if (char === '}') jsonDepth--;
      }

      console.log(`LINE: "${trimmedLine}" | beforeDepth=${beforeDepth} | afterDepth=${jsonDepth} | section=${section}`);

      if (trimmedLine === '}' && beforeDepth <= 1) {
        if (beforeDepth === 0) {
          console.log('  -> SKIP (section closer)');
          continue;
        } else {
          console.log('  -> JSON closer, INCLUDE');
        }
      }

      if (trimmedLine && !trimmedLine.startsWith('//')) {
        request.bodyText = (request.bodyText || '') + trimmedLine + '\n';
      }
    }
  }
  return request;
}

console.log('\n=== SIGNIN ===');
const signin = fs.readFileSync('auth/signin-positive.bru', 'utf-8');
parseBruFile(signin);

console.log('\n=== SIGNUP ===');
const signup = fs.readFileSync('auth/signup-positive.bru', 'utf-8');
parseBruFile(signup);
