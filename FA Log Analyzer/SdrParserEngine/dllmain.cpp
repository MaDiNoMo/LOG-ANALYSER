#include "pch.h"
#include <fstream>
#include <string>
#include <vector>
#include <regex>
#include <sstream>
#include <algorithm>

extern "C" {

    // UPDATED CALLBACK: Now includes 'protocol' and generic params (p1, p2, p3)
    typedef void (*ResultCallback)(
        bool isExpectedTree, const char* timestamp, const char* protocol,
        const char* p1, const char* p2, const char* p3, const char* colorHex);

    typedef void (*SummaryCallback)(int perfectMatches, int totalExpected, int extraMessages);

    struct ExpectedEvent { std::string protocol, p1, p2, p3; };
    struct ActualEvent { std::string timestamp, protocol, p1, p2, p3; };

    void CleanString(std::string& s) {
        if (s.empty()) return;
        size_t first = s.find_first_not_of(" \t\r\n\"\'"); // Added quotes to clean ASCII tags
        if (first == std::string::npos) { s.clear(); return; }
        size_t last = s.find_last_not_of(" \t\r\n\"\'");
        s = s.substr(first, last - first + 1);
    }

    bool ExtractValue(const std::string& line, std::string& val) {
        if (line.find("<U") != std::string::npos || line.find("<I") != std::string::npos || line.find("<A") != std::string::npos) {
            size_t firstQuote = line.find('\"');
            size_t lastQuote = line.rfind('\"');
            if (firstQuote != std::string::npos && lastQuote != std::string::npos && firstQuote < lastQuote) {
                val = line.substr(firstQuote + 1, lastQuote - firstQuote - 1);
                return true;
            }
            else {
                size_t start = line.find(' ');
                size_t end = line.rfind('>');
                if (start != std::string::npos && end != std::string::npos && start < end) {
                    val = line.substr(start + 1, end - start - 1);
                    CleanString(val);
                    return true;
                }
            }
        }
        return false;
    }

    __declspec(dllexport) void CompareLogSequence(
        const char* filePath, const char* startTime, const char* endTime,
        const char* expectedListStr, ResultCallback itemCallback, SummaryCallback summaryCallback)
    {
        // 1. Unpack Expected List (Format: Protocol,p1,p2,p3;Protocol,p1,p2,p3;)
        std::vector<ExpectedEvent> expectedEvents;
        std::stringstream ss(expectedListStr);
        std::string token;
        while (std::getline(ss, token, ';')) {
            if (token.empty()) continue;
            std::stringstream ssItem(token);
            std::string prot, p1, p2, p3;
            std::getline(ssItem, prot, ','); std::getline(ssItem, p1, ','); std::getline(ssItem, p2, ','); std::getline(ssItem, p3, ',');
            CleanString(prot); CleanString(p1); CleanString(p2); CleanString(p3);
            expectedEvents.push_back({ prot, p1, p2, p3 });
        }

        // 2. Fast Log Parse with Multi-Protocol State Machine
        std::ifstream file(filePath);
        if (!file.is_open()) return;

        std::vector<ActualEvent> actualEvents;
        std::string line, lastTs = "";
        std::string startT(startTime), endT(endTime);
        bool useTimeFilter = (!startT.empty() && !endT.empty());

        bool capturing = false;
        int valCount = 0;
        std::string curProt = "", curP1 = "", curP2 = "", curP3 = "";

        while (std::getline(file, line)) {
            if (line.length() >= 19 && line[10] == ' ') lastTs = line.substr(0, 19);

            if (line.find("S6F11") != std::string::npos && line.find("S6F11_") == std::string::npos) { capturing = true; curProt = "S6F11"; valCount = 0; }
            else if (line.find("S3F17") != std::string::npos && line.find("S3F17_") == std::string::npos) { capturing = true; curProt = "S3F17"; valCount = 0; }
            else if (line.find("S14F9") != std::string::npos && line.find("S14F9_") == std::string::npos) { capturing = true; curProt = "S14F9"; valCount = 0; }
            else if (line.find("S16F15") != std::string::npos && line.find("S16F15_") == std::string::npos) { capturing = true; curProt = "S16F15"; valCount = 0; }
            else if (line.find("S14F15") != std::string::npos && line.find("S14F15_") == std::string::npos) { capturing = true; curProt = "S14F15"; valCount = 0; }

            else if (capturing) {
                std::string val;
                if (ExtractValue(line, val)) {
                    valCount++;
                    bool msgComplete = false;

                    if (curProt == "S6F11") {
                        if (valCount == 1) curP1 = val; else if (valCount == 2) curP2 = val; else if (valCount == 3) { curP3 = val; msgComplete = true; }
                    }
                    else if (curProt == "S3F17") {
                        if (valCount == 1) curP1 = val; else if (valCount == 2) curP2 = val; else if (valCount == 3) curP3 = val; else if (valCount == 4) msgComplete = true;
                    }
                    else if (curProt == "S14F9") {
                        if (valCount == 1) curP1 = val; else if (valCount == 2) curP2 = val; else if (valCount == 4) curP3 = val; else if (valCount == 8) msgComplete = true;
                    }
                    else if (curProt == "S16F15") {
                        if (valCount == 1) curP1 = val; else if (valCount == 2) curP2 = val; else if (valCount == 3) { curP3 = val; msgComplete = true; }
                    }
                    else if (curProt == "S14F15") {
                        if (valCount == 1) curP1 = val; else if (valCount == 2) curP2 = val; else if (valCount == 3) { curP3 = val; msgComplete = true; }
                    }

                    if (msgComplete) {
                        bool inTime = (!useTimeFilter || (lastTs >= startT && lastTs <= endT));
                        if (inTime) actualEvents.push_back({ lastTs, curProt, curP1, curP2, curP3 });
                        capturing = false;
                    }
                }
            }
        }

        // 3. GIT DIFF MATH (Longest Common Subsequence)
        int expSize = static_cast<int>(expectedEvents.size());
        int actSize = static_cast<int>(actualEvents.size());
        std::vector<std::vector<int>> dp(expSize + 1, std::vector<int>(actSize + 1, 0));

        for (int i = 1; i <= expSize; i++) {
            for (int j = 1; j <= actSize; j++) {
                auto& e = expectedEvents[i - 1];
                auto& a = actualEvents[j - 1];

                bool match = (e.protocol == a.protocol) &&
                    (e.p1 == "" || e.p1 == "*" || e.p1 == a.p1) &&
                    (e.p2 == "" || e.p2 == "*" || e.p2 == a.p2) &&
                    (e.p3 == "" || e.p3 == "*" || e.p3 == a.p3);

                if (match) dp[i][j] = dp[i - 1][j - 1] + 1;
                else dp[i][j] = (std::max)(dp[i - 1][j], dp[i][j - 1]);
            }
        }

        struct DiffRow { int expIdx, actIdx; std::string status; };
        std::vector<DiffRow> rows;
        int r = expSize, c = actSize;

        while (r > 0 || c > 0) {
            auto& e = r > 0 ? expectedEvents[r - 1] : ExpectedEvent();
            auto& a = c > 0 ? actualEvents[c - 1] : ActualEvent();

            bool match = (r > 0 && c > 0) && (e.protocol == a.protocol) &&
                (e.p1 == "" || e.p1 == "*" || e.p1 == a.p1) &&
                (e.p2 == "" || e.p2 == "*" || e.p2 == a.p2) &&
                (e.p3 == "" || e.p3 == "*" || e.p3 == a.p3);

            if (match) { rows.push_back({ r - 1, c - 1, "Match" }); r--; c--; }
            else if (c > 0 && (r == 0 || dp[r][c - 1] >= dp[r - 1][c])) { rows.push_back({ -1, c - 1, "Extra" }); c--; }
            else { rows.push_back({ r - 1, -1, "Missing" }); r--; }
        }
        std::reverse(rows.begin(), rows.end());

        // Combine Missing & Extra into "Mismatch"
        for (size_t i = 0; i < rows.size(); i++) {
            if (i < rows.size() - 1) {
                if (rows[i].status == "Missing" && rows[i + 1].status == "Extra") {
                    rows[i].actIdx = rows[i + 1].actIdx; rows[i].status = "Mismatch";
                    rows.erase(rows.begin() + i + 1); i--;
                }
                else if (rows[i].status == "Extra" && rows[i + 1].status == "Missing") {
                    rows[i].expIdx = rows[i + 1].expIdx; rows[i].status = "Mismatch";
                    rows.erase(rows.begin() + i + 1); i--;
                }
            }
        }

        // 4. Send Results to UI
        int perfectMatches = 0;
        int extraMessages = 0;

        for (const auto& row : rows) {
            if (row.status == "Match") {
                auto& e = expectedEvents[row.expIdx]; auto& a = actualEvents[row.actIdx];
                itemCallback(true, "", e.protocol.c_str(), e.p1.c_str(), e.p2.c_str(), e.p3.c_str(), "#98C379");
                itemCallback(false, a.timestamp.c_str(), a.protocol.c_str(), a.p1.c_str(), a.p2.c_str(), a.p3.c_str(), "#98C379");
                perfectMatches++;
            }
            else if (row.status == "Missing") {
                auto& e = expectedEvents[row.expIdx];
                itemCallback(true, "", e.protocol.c_str(), e.p1.c_str(), e.p2.c_str(), e.p3.c_str(), "#E06C75");
                itemCallback(false, "", "", "", "", "", "#BLANK");
            }
            else if (row.status == "Extra") {
                auto& a = actualEvents[row.actIdx];
                itemCallback(true, "", "", "", "", "", "#BLANK");
                itemCallback(false, a.timestamp.c_str(), a.protocol.c_str(), a.p1.c_str(), a.p2.c_str(), a.p3.c_str(), "#E5C07B");
                extraMessages++;
            }
            else if (row.status == "Mismatch") {
                auto& e = expectedEvents[row.expIdx]; auto& a = actualEvents[row.actIdx];
                itemCallback(true, "", e.protocol.c_str(), e.p1.c_str(), e.p2.c_str(), e.p3.c_str(), "#C678DD");
                itemCallback(false, a.timestamp.c_str(), a.protocol.c_str(), a.p1.c_str(), a.p2.c_str(), a.p3.c_str(), "#C678DD");
            }
        }
        summaryCallback(perfectMatches, expSize, extraMessages);
    }
}