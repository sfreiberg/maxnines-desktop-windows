#!/bin/sh
# Prints the Windows Installer version for an app version such as 0.1.0-alpha.3.
#
# An installer's version is three numbers, and it only replaces an installed copy
# with a higher one, so pre-releases have to fit in and still sort right. The third
# number is the patch times 1000, plus 1 to 299 for alpha.1 to alpha.299, 300 more
# for beta and 600 more for rc, and 999 for the release itself:
#
#   0.1.0-alpha.3  ->  0.1.3
#   0.1.0-rc.1     ->  0.1.601
#   0.1.0          ->  0.1.999
#   0.1.1-alpha.1  ->  0.1.1001
set -eu

version=$1
core=${version%%-*}
suffix=${version#"$core"}
IFS=. read -r major minor patch <<EOV
$core
EOV

fail() {
    echo "$version can't be an installer version: $1" >&2
    exit 1
}

number() {
    case $1 in
        '' | *[!0-9]*) fail "$2" ;;
    esac
}

number "$major" "the major version isn't a number"
number "$minor" "the minor version isn't a number"
number "${patch:-}" "it needs three numbers, like 0.1.0"

case $suffix in
    '') step=999 ;;
    -alpha.*) number "${suffix#-alpha.}" "alpha needs a number" && step=${suffix#-alpha.} ;;
    -beta.*) number "${suffix#-beta.}" "beta needs a number" && step=$((300 + ${suffix#-beta.})) ;;
    -rc.*) number "${suffix#-rc.}" "rc needs a number" && step=$((600 + ${suffix#-rc.})) ;;
    *) fail "the suffix has to be -alpha.N, -beta.N or -rc.N" ;;
esac

case $suffix in
    -*) n=${suffix##*.}; [ "$n" -ge 1 ] && [ "$n" -le 299 ] || fail "the pre-release number has to be 1 to 299" ;;
esac
[ "$major" -le 255 ] && [ "$minor" -le 255 ] || fail "the major and minor versions go up to 255"
build=$((patch * 1000 + step))
[ "$build" -le 65535 ] || fail "the patch version goes up to 64"

echo "$major.$minor.$build"
