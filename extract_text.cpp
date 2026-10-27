#include <iostream>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <algorithm>
#include <cctype>
#include <chrono>

#if defined(__has_include)
  #if __has_include(<filesystem>)
    #include <filesystem>
    #if defined(__cpp_lib_filesystem) || (defined(__cplusplus) && __cplusplus >= 201703L) || (defined(_MSVC_LANG) && _MSVC_LANG >= 201703L)
      namespace fs = std::filesystem;
    #else
      namespace fs = std::experimental::filesystem;
    #endif
  #elif __has_include(<experimental/filesystem>)
    #include <experimental/filesystem>
    namespace fs = std::experimental::filesystem;
  #endif
#else
  #include <filesystem>
  namespace fs = std::filesystem;
#endif

inline bool ci_char_eq(char a, char b) {
    return std::tolower(static_cast<unsigned char>(a)) == std::tolower(static_cast<unsigned char>(b));
}

size_t find_ci(const std::string& str, const std::string& target, size_t start = 0) {
    if (target.empty() || start >= str.size()) return std::string::npos;
    auto it = std::search(str.begin() + start, str.end(), target.begin(), target.end(), ci_char_eq);
    if (it != str.end()) {
        return std::distance(str.begin(), it);
    }
    return std::string::npos;
}

std::string trim(const std::string& s) {
    size_t start = 0;
    while (start < s.size() && std::isspace(static_cast<unsigned char>(s[start]))) {
        ++start;
    }
    size_t end = s.size();
    while (end > start && std::isspace(static_cast<unsigned char>(s[end - 1]))) {
        --end;
    }
    return s.substr(start, end - start);
}

std::string decode_xml_entities(std::string text) {
    static const std::vector<std::pair<std::string, std::string>> entities = {
        {"&quot;", "\""},
        {"&apos;", "'"},
        {"&amp;",  "&"},
        {"&lt;",   "<"},
        {"&gt;",   ">"}
    };

    for (const auto& entry : entities) {
        const std::string& entity = entry.first;
        const std::string& replacement = entry.second;
        size_t pos = 0;
        while ((pos = text.find(entity, pos)) != std::string::npos) {
            text.replace(pos, entity.length(), replacement);
            pos += replacement.length();
        }
    }
    return text;
}

bool extract_tag(const std::string& xml, const std::string& tag_name, std::string& out_content, size_t start = 0, size_t* out_next_pos = nullptr) {
    std::string open_prefix = "<" + tag_name;
    size_t open_tag = find_ci(xml, open_prefix, start);
    if (open_tag == std::string::npos) return false;

    size_t open_tag_end = xml.find('>', open_tag);
    if (open_tag_end == std::string::npos) return false;

    if (open_tag_end > 0 && xml[open_tag_end - 1] == '/') {
        out_content = "";
        if (out_next_pos) *out_next_pos = open_tag_end + 1;
        return true;
    }

    std::string close_tag = "</" + tag_name + ">";
    size_t close_pos = find_ci(xml, close_tag, open_tag_end + 1);
    if (close_pos == std::string::npos) return false;

    out_content = xml.substr(open_tag_end + 1, close_pos - (open_tag_end + 1));
    if (out_next_pos) *out_next_pos = close_pos + close_tag.length();
    return true;
}

bool is_xml_file(const fs::path& p) {
    std::string ext = p.extension().string();
    std::transform(ext.begin(), ext.end(), ext.begin(), [](unsigned char c) { return std::tolower(c); });
    return ext == ".xml";
}

bool extract_topic_codes(const std::string& xml, std::vector<std::string>& out_codes) {
    size_t topics_class_pos = find_ci(xml, "bip:topics:1.0");
    if (topics_class_pos == std::string::npos) return false;

    size_t codes_open = xml.rfind("<codes", topics_class_pos);
    if (codes_open == std::string::npos) return false;

    size_t codes_close = find_ci(xml, "</codes>", topics_class_pos);
    if (codes_close == std::string::npos) return false;

    std::string topics_section = xml.substr(codes_open, codes_close - codes_open);
    size_t c_pos = 0;
    while ((c_pos = find_ci(topics_section, "<code", c_pos)) != std::string::npos) {
        size_t tag_end = topics_section.find('>', c_pos);
        if (tag_end == std::string::npos) break;

        std::string code_tag = topics_section.substr(c_pos, tag_end - c_pos + 1);
        size_t attr_pos = find_ci(code_tag, "code=");
        if (attr_pos != std::string::npos) {
            size_t val_start = attr_pos + 5;
            if (val_start < code_tag.length()) {
                char q = code_tag[val_start];
                if (q == '"' || q == '\'') {
                    ++val_start;
                    size_t val_end = code_tag.find(q, val_start);
                    if (val_end != std::string::npos) {
                        std::string code_val = code_tag.substr(val_start, val_end - val_start);
                        code_val = trim(code_val);
                        if (!code_val.empty() && std::find(out_codes.begin(), out_codes.end(), code_val) == out_codes.end()) {
                            out_codes.push_back(code_val);
                        }
                    }
                }
            }
        }
        c_pos = tag_end + 1;
    }
    return !out_codes.empty();
}

bool process_file(const fs::path& input_path, const fs::path& output_path) {
    std::ifstream in(input_path, std::ios::binary);
    if (!in.is_open()) {
        std::cerr << "Error: Unable to open input file: " << input_path.string() << "\n";
        return false;
    }

    std::ostringstream ss;
    ss << in.rdbuf();
    std::string content = ss.str();
    in.close();

    std::string title;
    if (extract_tag(content, "title", title)) {
        title = trim(decode_xml_entities(title));
    }

    std::vector<std::string> paragraphs;
    std::string text_block;
    if (extract_tag(content, "text", text_block)) {
        size_t pos = 0;
        size_t next_pos = 0;
        std::string p_content;
        while (extract_tag(text_block, "p", p_content, pos, &next_pos)) {
            std::string cleaned_p = trim(decode_xml_entities(p_content));
            if (!cleaned_p.empty()) {
                paragraphs.push_back(cleaned_p);
            }
            pos = next_pos;
        }
    }

    std::vector<std::string> topic_codes;
    extract_topic_codes(content, topic_codes);

    if (output_path.has_parent_path()) {
        std::error_code ec;
        fs::create_directories(output_path.parent_path(), ec);
    }

    std::ofstream out(output_path, std::ios::trunc);
    if (!out.is_open()) {
        std::cerr << "Error: Unable to open output file: " << output_path.string() << "\n";
        return false;
    }

    if (!title.empty()) {
        out << title << "\n";
    }

    for (const auto& p : paragraphs) {
        out << p << "\n";
    }

    for (const auto& tc : topic_codes) {
        out << "<code code=\"" << tc << "\">\n";
    }

    out.close();
    return true;
}

void print_usage(const char* prog_name) {
    std::cout << "Usage:\n"
              << "  " << prog_name << " [options]\n"
              << "  " << prog_name << " <input_dir_or_file> [output_dir]\n\n"
              << "Arguments:\n"
              << "  input_dir_or_file  Path to directory containing XML files, or a single XML file.\n"
              << "                     (Default: Reuters)\n"
              << "  output_dir         Path to destination directory for .txt files.\n"
              << "                     (Default: Reuters_txt)\n\n"
              << "Options:\n"
              << "  -h, --help         Show this help message.\n";
}

int main(int argc, char* argv[]) {
    fs::path input_path = "Reuters";
    fs::path output_path = "Reuters_txt";

    if (argc > 1) {
        std::string arg1 = argv[1];
        if (arg1 == "-h" || arg1 == "--help" || arg1 == "/?") {
            print_usage(argv[0]);
            return 0;
        }
        input_path = arg1;
    }

    if (argc > 2) {
        output_path = argv[2];
    }

    if (!fs::exists(input_path)) {
        std::cerr << "Error: Input path does not exist: " << input_path.string() << "\n";
        return 1;
    }

    auto start_time = std::chrono::high_resolution_clock::now();

    if (fs::is_regular_file(input_path)) {
        fs::path out_file = output_path;
        if (fs::is_directory(output_path) || output_path.extension().empty()) {
            out_file = output_path / (input_path.stem().string() + ".txt");
        }
        std::cout << "Processing single file: " << input_path.filename().string() << " -> " << out_file.string() << "\n";
        if (process_file(input_path, out_file)) {
            std::cout << "Successfully extracted text.\n";
            return 0;
        } else {
            return 1;
        }
    }

    std::cout << "Scanning directory: " << input_path.string() << "\n";
    std::cout << "Output directory:   " << output_path.string() << "\n";

    std::vector<fs::path> xml_files;
    for (const auto& entry : fs::recursive_directory_iterator(input_path)) {
        if (entry.is_regular_file() && is_xml_file(entry.path())) {
            xml_files.push_back(entry.path());
        }
    }

    size_t total_files = xml_files.size();
    std::cout << "Found " << total_files << " XML file(s) to process.\n";

    if (total_files == 0) {
        std::cout << "No XML files found.\n";
        return 0;
    }

    size_t success_count = 0;
    size_t error_count = 0;

    for (size_t i = 0; i < total_files; ++i) {
        const auto& in_file = xml_files[i];

        fs::path rel_path = fs::relative(in_file, input_path);
        fs::path out_file = output_path / rel_path;
        out_file.replace_extension(".txt");

        if (process_file(in_file, out_file)) {
            ++success_count;
        } else {
            ++error_count;
        }

        if ((i + 1) % 1000 == 0 || (i + 1) == total_files) {
            std::cout << "[" << (i + 1) << "/" << total_files << "] files processed...\n";
        }
    }

    auto end_time = std::chrono::high_resolution_clock::now();
    std::chrono::duration<double> elapsed = end_time - start_time;

    std::cout << "\n================ Summary ================\n";
    std::cout << "Total processed: " << success_count << "\n";
    if (error_count > 0) {
        std::cout << "Errors:          " << error_count << "\n";
    }
    std::cout << "Elapsed time:    " << elapsed.count() << " seconds\n";
    std::cout << "Output location: " << fs::absolute(output_path).string() << "\n";
    std::cout << "=========================================\n";

    return (error_count == 0) ? 0 : 1;
}
