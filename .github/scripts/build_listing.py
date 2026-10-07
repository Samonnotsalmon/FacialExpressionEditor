"""
GitHub のリリース（パッケージの zip と package.json があるもの）から、VCC 用の一覧（index.json）と、
VCC に追加するためのページ（index.html）を作る。GitHub Actions から呼ぶ（GITHUB_REPOSITORY と GH_TOKEN を使う）。

使い方: python3 build_listing.py 出力フォルダ
"""
import hashlib
import html
import json
import os
import sys
import urllib.parse
import urllib.request


def api(path):
    request = urllib.request.Request(
        f"https://api.github.com{path}",
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {os.environ['GH_TOKEN']}",
            "User-Agent": "build-listing",
        },
    )
    with urllib.request.urlopen(request) as response:
        return json.loads(response.read())


def download(url):
    # 公開リポジトリのリリースの添付ファイルは、認証なしで取れる（認証を付けると転送先で断られる）。
    request = urllib.request.Request(url, headers={"User-Agent": "build-listing"})
    with urllib.request.urlopen(request) as response:
        return response.read()


def releases(repository):
    result = []
    page = 1
    while True:
        batch = api(f"/repos/{repository}/releases?per_page=100&page={page}")
        if not batch:
            return result
        result.extend(batch)
        page += 1


def build(repository, release_list, fetch, author):
    """
    リリースの一覧から VCC 用の一覧を作る。下書きと、zip か package.json が無いリリースは使わない。
    """
    owner, name = repository.split("/")
    packages = {}
    for release in release_list:
        if release.get("draft"):
            continue
        assets = {asset["name"]: asset for asset in release.get("assets", [])}
        manifest_asset = assets.get("package.json")
        if manifest_asset is None:
            continue
        manifest = json.loads(fetch(manifest_asset["browser_download_url"]))
        zip_asset = assets.get(f"{manifest['name']}-{manifest['version']}.zip")
        if zip_asset is None:
            continue

        manifest["url"] = zip_asset["browser_download_url"]
        manifest["zipSHA256"] = hashlib.sha256(fetch(zip_asset["browser_download_url"])).hexdigest()
        packages.setdefault(manifest["name"], {"versions": {}})["versions"][manifest["version"]] = manifest

    return {
        "name": f"{author}'s Packages",
        "id": f"io.github.{owner.lower()}.{name.lower()}",
        "url": f"https://{owner.lower()}.github.io/{name}/index.json",
        "author": author,
        "packages": packages,
    }


def page(listing):
    url = listing["url"]
    add = "vcc://vpm/addRepo?url=" + urllib.parse.quote(url, safe="")
    rows = "".join(
        f"<li>{html.escape(package)}：{html.escape(', '.join(sorted(entry['versions'], reverse=True)))}</li>"
        for package, entry in listing["packages"].items()
    )
    return f"""<!doctype html>
<html lang="ja">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{html.escape(listing['name'])}</title></head>
<body style="font-family: sans-serif; max-width: 40em; margin: 2em auto; padding: 0 1em;">
<h1>{html.escape(listing['name'])}</h1>
<p><a href="{html.escape(add)}">VCC に追加する</a></p>
<p>うまくいかないときは、VCC の Settings → Packages → Add Repository に次の URL を入れてください。</p>
<pre>{html.escape(url)}</pre>
<ul>{rows}</ul>
</body>
</html>
"""


def main():
    output = sys.argv[1]
    repository = os.environ["GITHUB_REPOSITORY"]
    with open("package.json", encoding="utf-8") as f:
        author = json.load(f).get("author", {}).get("name") or repository.split("/")[0]

    listing = build(repository, releases(repository), download, author)
    os.makedirs(output, exist_ok=True)
    with open(os.path.join(output, "index.json"), "w", encoding="utf-8") as f:
        json.dump(listing, f, ensure_ascii=False, indent=2)
    with open(os.path.join(output, "index.html"), "w", encoding="utf-8") as f:
        f.write(page(listing))
    for package, entry in listing["packages"].items():
        print(f"{package}: {', '.join(entry['versions'])}")


if __name__ == "__main__":
    main()
